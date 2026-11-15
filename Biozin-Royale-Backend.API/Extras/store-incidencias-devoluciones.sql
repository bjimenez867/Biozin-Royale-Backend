-- =====================================================================
-- Biozin Royale · Tienda · Fase B: incidencias y devoluciones/cambios
-- PostgreSQL (Supabase). Idempotente: se puede ejecutar más de una vez.
--
-- A diferencia de store.sql (que solo creaba tablas nuevas), este script
-- SÍ altera una tabla existente: agrega una columna a store_orders (aditivo,
-- con DEFAULT, no toca filas ni columnas existentes). El resto son tablas
-- y configuración nuevas.
--
-- 1) store_orders.incident_count: cuenta los intentos de entrega fallidos;
--    RegistrarIncidenciaAsync la incrementa y bloquea un intento más allá
--    de max_delivery_attempts (antes el ciclo incidencia → reprogramar
--    podía repetirse sin límite).
-- 2) store_return_requests: solicitud de cambio de talla o devolución de
--    dinero por un artículo del pedido. Antes "pedir un cambio o
--    devolución" solo dejaba una línea en la bitácora sin ningún lugar
--    donde el admin pudiera aprobarla, rechazarla o resolverla.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
-- 1. Límite de intentos de entrega
-- ---------------------------------------------------------------------
ALTER TABLE public.store_orders
  ADD COLUMN IF NOT EXISTS incident_count smallint NOT NULL DEFAULT 0 CHECK (incident_count >= 0);

INSERT INTO public.store_settings (setting_key, setting_value, description) VALUES
  ('max_delivery_attempts', '3', 'Intentos de entrega fallidos antes de requerir una decisión manual (cancelar o contactar al cliente)')
ON CONFLICT (setting_key) DO NOTHING;

-- ---------------------------------------------------------------------
-- 2. Solicitudes de cambio de talla o devolución, por artículo del pedido.
--    request_type: 'exchange' (cambiar de talla; el admin coordina el
--    envío de la talla nueva) o 'refund' (devolver el dinero de ese
--    artículo: billetera se reembolsa sola, tarjeta queda para gestión
--    manual, igual que al cancelar un pedido pagado con tarjeta).
--    Una sola solicitud activa (pendiente o aprobada) por artículo.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_return_requests (
  id                           uuid          NOT NULL DEFAULT gen_random_uuid(),
  order_id                     uuid          NOT NULL,
  order_item_id                uuid          NOT NULL,
  request_type                 varchar(10)   NOT NULL CHECK (request_type IN ('exchange', 'refund')),
  reason                       varchar(40)   NOT NULL,
  detail                       varchar(300)  NOT NULL DEFAULT '',
  quantity                     integer       NOT NULL CHECK (quantity > 0),
  exchange_size                varchar(10),
  status                       varchar(10)   NOT NULL DEFAULT 'pending'
                               CHECK (status IN ('pending', 'approved', 'rejected')),
  resolution_note              text,
  refund_wallet_transaction_id uuid,
  resolved_by_id               uuid,
  resolved_by_name             varchar(120),
  resolved_at                  timestamp with time zone,
  created_at                   timestamp with time zone NOT NULL DEFAULT now(),
  updated_at                   timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_return_requests_pkey PRIMARY KEY (id),
  CONSTRAINT store_return_requests_order_id_fkey
    FOREIGN KEY (order_id) REFERENCES public.store_orders(id) ON DELETE CASCADE,
  CONSTRAINT store_return_requests_item_id_fkey
    FOREIGN KEY (order_item_id) REFERENCES public.store_order_items(id),
  CONSTRAINT store_return_requests_refund_tx_fkey
    FOREIGN KEY (refund_wallet_transaction_id) REFERENCES public.wallet_transactions(id),
  CONSTRAINT store_return_requests_exchange_size_chk
    CHECK (request_type = 'exchange' OR exchange_size IS NULL)
);
CREATE INDEX IF NOT EXISTS store_return_requests_order_idx ON public.store_return_requests (order_id);
CREATE INDEX IF NOT EXISTS store_return_requests_pending_idx ON public.store_return_requests (created_at) WHERE status = 'pending';
CREATE UNIQUE INDEX IF NOT EXISTS store_return_requests_active_uq
  ON public.store_return_requests (order_item_id) WHERE status IN ('pending', 'approved');

ALTER TABLE public.store_return_requests ENABLE ROW LEVEL SECURITY;

COMMENT ON TABLE public.store_return_requests IS
  'Cambio de talla o devolución de un artículo del pedido. refund + billetera se reembolsa sola (wallet_transactions, purchase_refund); refund + tarjeta y exchange quedan para que el admin los resuelva fuera del sistema.';

COMMIT;

-- =====================================================================
-- DESHACER (solo en desarrollo):
--
-- BEGIN;
-- DROP TABLE IF EXISTS public.store_return_requests CASCADE;
-- ALTER TABLE public.store_orders DROP COLUMN IF EXISTS incident_count;
-- DELETE FROM public.store_settings WHERE setting_key = 'max_delivery_attempts';
-- COMMIT;
-- =====================================================================
