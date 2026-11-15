-- =====================================================================
-- Biozin Royale · Tienda (store) · Fase A
-- PostgreSQL (Supabase). Idempotente: se puede ejecutar más de una vez.
-- Solo CREA objetos nuevos (store_*); no modifica ninguna tabla existente.
--
-- Referencia (FK) a tablas existentes: auth.users, wallet_transactions, payment_methods.
-- Convenciones del esquema actual:
--   · tablas en plural y snake_case, ids uuid con gen_random_uuid()
--   · estados con CHECK y lista cerrada
--   · "quién lo hizo": actor_id / actor_role / actor_name SIN FK (el staff vive en staff_members)
--   · la tienda solo la opera 'admin' (no hay soporte en este módulo)
--   · el backend mantiene updated_at (esta base no usa triggers para eso)
--
-- Billetera: los movimientos de la tienda usan wallet_transactions con
--   transaction_type 'purchase' / 'purchase_refund', status 'completed',
--   reference_type 'store_order', reference_id = store_orders.id.
--   Los lectores actuales (WalletLN, ReportesLN) filtran por 'deposit' y 'withdrawal',
--   por lo que estos movimientos no alteran finanzas ni reportes existentes.
--
-- Tarjeta: se elige una de payment_methods (type = 'card'). Esas tarjetas no tienen token de
--   pasarela, así que el pago queda 'pending' y el admin lo confirma (igual que el retiro con
--   tarjeta). store_settings.card_auto_confirm = 'true' lo confirma solo (solo para pruebas).
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
-- 1. Categorías
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_categories (
  id          uuid         NOT NULL DEFAULT gen_random_uuid(),
  name        varchar(80)  NOT NULL,
  slug        varchar(80)  NOT NULL,
  description varchar(200),
  sort_order  integer      NOT NULL DEFAULT 0,
  is_active   boolean      NOT NULL DEFAULT true,
  created_at  timestamp with time zone NOT NULL DEFAULT now(),
  updated_at  timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_categories_pkey     PRIMARY KEY (id),
  CONSTRAINT store_categories_name_key UNIQUE (name),
  CONSTRAINT store_categories_slug_key UNIQUE (slug)
);

-- ---------------------------------------------------------------------
-- 2. Productos
--    art_type / art_variant: ilustración provisional del frontend mientras no
--    existan fotos. Con fotos se usa image_path (Supabase Storage).
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_products (
  id            uuid          NOT NULL DEFAULT gen_random_uuid(),
  sku           varchar(40)   NOT NULL,
  name          varchar(120)  NOT NULL,
  category_id   uuid          NOT NULL,
  description   text          NOT NULL DEFAULT '',
  price         numeric(12,2) NOT NULL CHECK (price > 0),
  image_path    text,
  art_type      varchar(20),
  art_variant   varchar(20),
  badge         varchar(30)   NOT NULL DEFAULT '',
  requires_size boolean       NOT NULL DEFAULT false,
  is_visible    boolean       NOT NULL DEFAULT true,
  deleted_at    timestamp with time zone,
  created_at    timestamp with time zone NOT NULL DEFAULT now(),
  updated_at    timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_products_pkey PRIMARY KEY (id),
  CONSTRAINT store_products_sku_key UNIQUE (sku),
  CONSTRAINT store_products_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.store_categories(id)
);
CREATE INDEX IF NOT EXISTS store_products_category_idx ON public.store_products (category_id);
CREATE INDEX IF NOT EXISTS store_products_visible_idx  ON public.store_products (is_visible) WHERE deleted_at IS NULL;

-- ---------------------------------------------------------------------
-- 3. Variantes (talla) y stock. Un producto sin talla tiene una sola variante con size NULL.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_product_variants (
  id            uuid        NOT NULL DEFAULT gen_random_uuid(),
  product_id    uuid        NOT NULL,
  size          varchar(10),
  sku           varchar(50) NOT NULL,
  stock         integer     NOT NULL DEFAULT 0 CHECK (stock >= 0),
  reorder_level integer     NOT NULL DEFAULT 5 CHECK (reorder_level >= 0),
  is_active     boolean     NOT NULL DEFAULT true,
  created_at    timestamp with time zone NOT NULL DEFAULT now(),
  updated_at    timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_product_variants_pkey PRIMARY KEY (id),
  CONSTRAINT store_product_variants_sku_key UNIQUE (sku),
  CONSTRAINT store_product_variants_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.store_products(id)
);
CREATE UNIQUE INDEX IF NOT EXISTS store_product_variants_size_uq ON public.store_product_variants (product_id, COALESCE(size, ''));

-- ---------------------------------------------------------------------
-- 4. Pedidos
--    fulfillment_phase: 0 recibido, 1 pago confirmado, 2 en preparación, 3 empacado,
--    4 con el transportista, 5 en reparto, 6 entregado.
--    delivery_code: 4 dígitos que el cliente da al repartidor; code_attempts +
--    code_locked_until frenan la fuerza bruta (solo hay 10.000 combinaciones).
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_orders (
  id                uuid          NOT NULL DEFAULT gen_random_uuid(),
  order_number      integer       GENERATED BY DEFAULT AS IDENTITY (START WITH 10242) NOT NULL,
  user_id           uuid          NOT NULL,
  customer_name     varchar(120)  NOT NULL,
  phone             varchar(20)   NOT NULL,
  province          varchar(40)   NOT NULL,
  canton            varchar(60),
  district          varchar(60),
  address_line      text          NOT NULL,
  subtotal          numeric(12,2) NOT NULL CHECK (subtotal >= 0),
  shipping_cost     numeric(12,2) NOT NULL DEFAULT 0 CHECK (shipping_cost >= 0),
  total             numeric(12,2) NOT NULL CHECK (total >= 0),
  payment_status    varchar(15)   NOT NULL DEFAULT 'pending'
                    CHECK (payment_status IN ('pending', 'confirmed', 'rejected', 'refunded')),
  fulfillment_phase smallint      NOT NULL DEFAULT 0 CHECK (fulfillment_phase BETWEEN 0 AND 6),
  is_cancelled      boolean       NOT NULL DEFAULT false,
  cancelled_at      timestamp with time zone,
  cancelled_by_id   uuid,
  cancelled_by_role varchar(15),
  cancel_reason     text,
  carrier_name      varchar(80),
  tracking_number   varchar(40),
  package_info      varchar(60),
  delivery_code     varchar(4)    NOT NULL CHECK (delivery_code ~ '^[0-9]{4}$'),
  code_attempts     smallint      NOT NULL DEFAULT 0 CHECK (code_attempts >= 0),
  code_locked_until timestamp with time zone,
  has_open_incident boolean       NOT NULL DEFAULT false,
  reschedule_window varchar(60),
  eta_from          date,
  eta_to            date,
  rating            smallint      CHECK (rating >= 1 AND rating <= 5),
  rated_at          timestamp with time zone,
  idempotency_key   varchar(64),
  created_at        timestamp with time zone NOT NULL DEFAULT now(),
  updated_at        timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_orders_pkey PRIMARY KEY (id),
  CONSTRAINT store_orders_order_number_key UNIQUE (order_number),
  CONSTRAINT store_orders_user_id_fkey FOREIGN KEY (user_id) REFERENCES auth.users(id),
  CONSTRAINT store_orders_total_chk   CHECK (total = subtotal + shipping_cost),
  CONSTRAINT store_orders_cancel_chk  CHECK (NOT is_cancelled OR cancelled_at IS NOT NULL),
  CONSTRAINT store_orders_eta_chk     CHECK (eta_to IS NULL OR eta_from IS NULL OR eta_to >= eta_from),
  CONSTRAINT store_orders_cancel_role_chk CHECK (cancelled_by_role IS NULL OR cancelled_by_role IN ('user', 'authenticated', 'admin', 'system'))
);
CREATE INDEX IF NOT EXISTS store_orders_user_idx    ON public.store_orders (user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS store_orders_work_idx    ON public.store_orders (is_cancelled, fulfillment_phase);
CREATE INDEX IF NOT EXISTS store_orders_pending_idx ON public.store_orders (created_at) WHERE payment_status = 'pending' AND is_cancelled = false;
-- evita crear dos pedidos si el cliente reintenta la misma compra
CREATE UNIQUE INDEX IF NOT EXISTS store_orders_idempotency_uq ON public.store_orders (user_id, idempotency_key) WHERE idempotency_key IS NOT NULL;

-- ---------------------------------------------------------------------
-- 5. Artículos del pedido (precio y nombre congelados al comprar)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_order_items (
  id           uuid          NOT NULL DEFAULT gen_random_uuid(),
  order_id     uuid          NOT NULL,
  product_id   uuid          NOT NULL,
  variant_id   uuid          NOT NULL,
  product_name varchar(120)  NOT NULL,
  size         varchar(10),
  unit_price   numeric(12,2) NOT NULL CHECK (unit_price >= 0),
  quantity     integer       NOT NULL CHECK (quantity > 0),
  created_at   timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_order_items_pkey PRIMARY KEY (id),
  CONSTRAINT store_order_items_order_id_fkey   FOREIGN KEY (order_id)   REFERENCES public.store_orders(id) ON DELETE CASCADE,
  CONSTRAINT store_order_items_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.store_products(id),
  CONSTRAINT store_order_items_variant_id_fkey FOREIGN KEY (variant_id) REFERENCES public.store_product_variants(id)
);
CREATE INDEX IF NOT EXISTS store_order_items_order_idx ON public.store_order_items (order_id);

-- ---------------------------------------------------------------------
-- 6. Pagos del pedido
--    wallet: se enlaza con la transacción de la billetera (purchase / purchase_refund).
--    card:   se elige una tarjeta de payment_methods; se copian últimos 4 y red para que el
--            pedido la siga mostrando si el usuario la borra después.
--    El pago con billetera se confirma al instante; el de tarjeta lo confirma el admin.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_payments (
  id                           uuid          NOT NULL DEFAULT gen_random_uuid(),
  order_id                     uuid          NOT NULL,
  method                       varchar(10)   NOT NULL CHECK (method IN ('wallet', 'card', 'paypal')),
  status                       varchar(15)   NOT NULL DEFAULT 'pending'
                               CHECK (status IN ('pending', 'confirmed', 'rejected', 'refunded')),
  amount                       numeric(12,2) NOT NULL CHECK (amount > 0),
  wallet_transaction_id        uuid,
  refund_wallet_transaction_id uuid,
  payment_method_id            uuid,
  card_last_four               varchar(4),
  card_network                 varchar(30),
  provider                     varchar(20),
  provider_ref                 varchar(120),
  verified_by_id               uuid,
  verified_by_name             varchar(120),
  verified_at                  timestamp with time zone,
  rejected_reason              text,
  refunded_at                  timestamp with time zone,
  created_at                   timestamp with time zone NOT NULL DEFAULT now(),
  updated_at                   timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_payments_pkey PRIMARY KEY (id),
  CONSTRAINT store_payments_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.store_orders(id) ON DELETE CASCADE,
  CONSTRAINT store_payments_wallet_tx_fkey FOREIGN KEY (wallet_transaction_id) REFERENCES public.wallet_transactions(id),
  CONSTRAINT store_payments_refund_tx_fkey FOREIGN KEY (refund_wallet_transaction_id) REFERENCES public.wallet_transactions(id),
  CONSTRAINT store_payments_method_fkey FOREIGN KEY (payment_method_id) REFERENCES public.payment_methods(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS store_payments_order_idx ON public.store_payments (order_id);
-- un pedido solo puede tener un pago confirmado
CREATE UNIQUE INDEX IF NOT EXISTS store_payments_one_confirmed_uq ON public.store_payments (order_id) WHERE status = 'confirmed';
-- una referencia de pasarela no se repite
CREATE UNIQUE INDEX IF NOT EXISTS store_payments_provider_ref_uq ON public.store_payments (provider, provider_ref) WHERE provider_ref IS NOT NULL;

-- ---------------------------------------------------------------------
-- 7. Bitácora del pedido: qué pasó, quién lo hizo y cuándo.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_order_events (
  id         uuid         NOT NULL DEFAULT gen_random_uuid(),
  order_id   uuid         NOT NULL,
  event_type varchar(20)  NOT NULL
             CHECK (event_type IN ('phase', 'payment', 'incident', 'reschedule', 'cancellation', 'change', 'rating')),
  phase      smallint     CHECK (phase BETWEEN 0 AND 6),
  message    text         NOT NULL,
  actor_id   uuid,
  actor_role varchar(15)  NOT NULL
             CHECK (actor_role IN ('user', 'authenticated', 'admin', 'system')),
  actor_name varchar(120) NOT NULL,
  metadata   jsonb,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_order_events_pkey PRIMARY KEY (id),
  CONSTRAINT store_order_events_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.store_orders(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS store_order_events_order_idx ON public.store_order_events (order_id, created_at);

-- ---------------------------------------------------------------------
-- 8. Avisos del cliente (lista con leído / no leído).
--    Los avisos del admin no se guardan: salen del estado de los pedidos.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_notifications (
  id                uuid         NOT NULL DEFAULT gen_random_uuid(),
  user_id           uuid         NOT NULL,
  notification_type varchar(30)  NOT NULL DEFAULT 'order',
  title             varchar(120) NOT NULL,
  body              text         NOT NULL,
  reference_type    varchar(30),
  reference_id      uuid,
  read_at           timestamp with time zone,
  created_at        timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_notifications_pkey PRIMARY KEY (id),
  CONSTRAINT store_notifications_user_id_fkey FOREIGN KEY (user_id) REFERENCES auth.users(id)
);
CREATE INDEX IF NOT EXISTS store_notifications_user_idx   ON public.store_notifications (user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS store_notifications_unread_idx ON public.store_notifications (user_id) WHERE read_at IS NULL;

-- ---------------------------------------------------------------------
-- 9. Configuración de la tienda (la edita el admin)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.store_settings (
  setting_key   varchar(60) NOT NULL,
  setting_value text        NOT NULL,
  description   text,
  updated_at    timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT store_settings_pkey PRIMARY KEY (setting_key)
);

INSERT INTO public.store_settings (setting_key, setting_value, description) VALUES
  ('free_shipping_from',  '60',    'Envío gratis desde este subtotal (USD)'),
  ('shipping_cost',       '5',     'Costo del envío a domicilio cuando no es gratis (USD)'),
  ('return_window_days',  '15',    'Días para pedir cambio o devolución'),
  ('payment_hold_hours',  '24',    'Horas que un pedido con pago pendiente espera antes de cancelarse'),
  ('code_max_attempts',   '5',     'Intentos de código de entrega antes de bloquear'),
  ('card_auto_confirm',   'false', 'true = el pago con tarjeta se confirma solo (solo pruebas); false = lo confirma el admin')
ON CONFLICT (setting_key) DO NOTHING;

-- ---------------------------------------------------------------------
-- 10. Seguridad por fila. Todas las tablas del proyecto ya tienen RLS activado.
--     Sin políticas, la API REST de Supabase (clave publicable) no puede leer ni escribir
--     estas tablas; el backend se conecta como postgres y no se ve afectado.
-- ---------------------------------------------------------------------
ALTER TABLE public.store_categories        ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.store_products          ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.store_product_variants  ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.store_orders            ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.store_order_items       ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.store_payments          ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.store_order_events      ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.store_notifications     ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.store_settings          ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------
-- 11. Datos iniciales: catálogo del demo (categorías, productos y stock por talla).
--     Camisetas y sudaderas llevan 4 tallas (S, M, L, XL); el stock del demo se reparte
--     en partes iguales y el sobrante queda en M. Re-ejecutable: no duplica nada.
-- ---------------------------------------------------------------------
INSERT INTO public.store_categories (name, slug, sort_order) VALUES
  ('Camisetas',          'camisetas',         1),
  ('Sudaderas y gorras', 'sudaderas-gorras',  2),
  ('Accesorios',         'accesorios',        3),
  ('Hogar',              'hogar',             4),
  ('Juegos',             'juegos',            5)
ON CONFLICT (slug) DO NOTHING;

CREATE TEMP TABLE _seed_products (
  code varchar(10), name varchar(120), cat varchar(80), price numeric(12,2), art varchar(20),
  variant varchar(20), stock integer, badge varchar(30), descr text
) ON COMMIT DROP;

INSERT INTO _seed_products VALUES
  ('t1', 'Camiseta Royale',        'camisetas',        28, 'tee',   '#15110a', 24, 'Más vendido', 'Algodón negro con el emblema bordado al pecho.'),
  ('t2', 'Camiseta Marfil',        'camisetas',        30, 'tee',   '#ece3cb',  6, '',            'Color marfil con el emblema en dorado y negro.'),
  ('t3', 'Camiseta Ruleta',        'camisetas',        29, 'tee',   '#3a1319', 15, '',            'Vino profundo, inspirada en la mesa de ruleta.'),
  ('t4', 'Camiseta Mesa Verde',    'camisetas',        29, 'tee',   '#12261f', 11, '',            'Verde fieltro de mesa con el emblema dorado.'),
  ('t5', 'Camiseta Gold Member',   'camisetas',        34, 'tee',   '#3a2a0d',  9, 'Nuevo',       'Bronce oscuro, corte relajado y emblema grande.'),
  ('h1', 'Sudadera High Roller',   'sudaderas-gorras', 62, 'hood',  '#15110a',  3, '',            'Felpa gruesa con capucha y emblema bordado.'),
  ('h2', 'Sudadera Marfil',        'sudaderas-gorras', 64, 'hood',  '#ece3cb', 10, '',            'Felpa color marfil con interior suave.'),
  ('g1', 'Gorra Royale Negra',     'sudaderas-gorras', 24, 'cap',   '#15110a', 18, '',            'Ajustable, con el emblema bordado al frente.'),
  ('g2', 'Gorra Royale Marfil',    'sudaderas-gorras', 24, 'cap',   '#ece3cb', 12, '',            'Ajustable, color marfil con emblema dorado.'),
  ('g3', 'Gorra Vino',             'sudaderas-gorras', 26, 'cap',   '#3a1319', 14, 'Nuevo',       'Edición vino con bordado dorado.'),
  ('k1', 'Llavero Ficha 5',        'accesorios',        8, 'key',   '5',        2, '',            'Llavero de metal con la ficha de 5 de la casa.'),
  ('k2', 'Llavero Ficha 25',       'accesorios',        9, 'key',   '25',      22, '',            'Llavero de metal con la ficha de 25.'),
  ('k3', 'Llavero Ficha 100',      'accesorios',       11, 'key',   '100',     16, 'Nuevo',       'La ficha de 100 en versión llavero.'),
  ('r3', 'Pin Royale',             'accesorios',        6, 'pin',   '',        30, '',            'Pin esmaltado con el emblema para solapa o gorra.'),
  ('a1', 'Bolsa de tela',          'accesorios',       15, 'tote',  '#ece3cb', 26, '',            'Algodón grueso con el emblema estampado.'),
  ('r1', 'Taza Jackpot',           'hogar',            14, 'mug',   '#2b2210', 40, '',            'Cerámica de 330 ml con borde dorado.'),
  ('r4', 'Taza Marfil',            'hogar',            14, 'mug',   '#ece3cb', 33, '',            'Cerámica de 330 ml color marfil.'),
  ('p1', 'Póster Royale',          'hogar',            18, 'post',  '#15110a', 20, '',            'Impresión de 40 × 60 cm en papel mate.'),
  ('p2', 'Póster Marfil',          'hogar',            18, 'post',  '#ece3cb', 17, '',            'Versión marfil, 40 × 60 cm en papel mate.'),
  ('j1', 'Baraja Royale',          'juegos',           16, 'cards', '',        12, 'Más vendido', '54 cartas con dorso negro y dorado.'),
  ('j2', 'Set de 50 fichas',       'juegos',           39, 'chips', '',         8, '',            'Cincuenta fichas con estuche de aluminio.'),
  ('j3', 'Set de 100 fichas',      'juegos',           69, 'chips', 'mix',      5, '',            'Cien fichas de cinco valores en estuche.'),
  ('j4', 'Dados dorados',          'juegos',           12, 'dice',  '#15110a', 25, '',            'Dos dados de 16 mm con acabado dorado.'),
  ('j5', 'Tapete de póker',        'juegos',           45, 'mat',   '#12261f',  7, 'Nuevo',       'Fieltro verde de 90 cm con el emblema.');

INSERT INTO public.store_products (sku, name, category_id, description, price, art_type, art_variant, badge, requires_size)
SELECT 'BR-' || upper(s.code), s.name, c.id, s.descr, s.price, s.art, s.variant, s.badge, s.art IN ('tee', 'hood')
FROM _seed_products s
JOIN public.store_categories c ON c.slug = s.cat
ON CONFLICT (sku) DO NOTHING;

INSERT INTO public.store_product_variants (product_id, size, sku, stock)
SELECT p.id, v.size, CASE WHEN v.size IS NULL THEN p.sku ELSE p.sku || '-' || v.size END, v.stock
FROM _seed_products s
JOIN public.store_products p ON p.sku = 'BR-' || upper(s.code)
CROSS JOIN LATERAL (
  SELECT sz::varchar AS size,
         (s.stock / 4) + CASE WHEN sz = 'M' THEN s.stock - 4 * (s.stock / 4) ELSE 0 END AS stock
  FROM unnest(ARRAY['S', 'M', 'L', 'XL']) AS sz
  WHERE s.art IN ('tee', 'hood')
  UNION ALL
  SELECT NULL::varchar, s.stock
  WHERE s.art NOT IN ('tee', 'hood')
) v
ON CONFLICT (sku) DO NOTHING;

-- ---------------------------------------------------------------------
-- 12. Comentarios de documentación
-- ---------------------------------------------------------------------
COMMENT ON TABLE public.store_orders         IS 'Pedido de la tienda. payment_status y fulfillment_phase son estados independientes.';
COMMENT ON TABLE public.store_order_events   IS 'Bitácora del pedido. actor_* no tiene FK: el staff vive en staff_members.';
COMMENT ON TABLE public.store_payments       IS 'Pagos del pedido. Billetera: wallet_transactions (reference_type store_order). Tarjeta: payment_methods.';
COMMENT ON COLUMN public.store_orders.delivery_code IS 'Código de 4 dígitos que el cliente le da al repartidor al recibir el pedido.';

-- ---------------------------------------------------------------------
-- 13. Fotos de producto (Supabase Storage)
--
-- Bucket público 'store-products', igual que el de avatares. Los archivos
-- viven en la raíz del bucket como <sku en minúscula>.webp (br-t1.webp; ver
-- Extras/store-images) y el frontend arma la URL pública con image_path.
-- La subida de archivos se hace por la API de Storage o el panel (no por
-- SQL); ver Extras/store-images/README.md.
-- ---------------------------------------------------------------------
INSERT INTO storage.buckets (id, name, public)
VALUES ('store-products', 'store-products', true)
ON CONFLICT (id) DO UPDATE SET public = true;

UPDATE public.store_products
SET image_path = lower(sku) || '.webp'
WHERE image_path IS NULL;

COMMIT;

-- =====================================================================
-- DESHACER (solo en desarrollo): borra todo lo de la fase A
--
-- BEGIN;
-- DROP TABLE IF EXISTS public.store_notifications, public.store_order_events, public.store_payments,
--   public.store_order_items, public.store_orders, public.store_product_variants,
--   public.store_products, public.store_categories, public.store_settings CASCADE;
-- COMMIT;
-- =====================================================================
