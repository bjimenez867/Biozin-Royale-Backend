# Fotos de producto de la tienda

Imágenes de los 24 productos sembrados por `../store.sql` (1000×1000, WebP,
estilo de la casa). El nombre de cada archivo es el SKU en minúscula:
`br-t1.webp` → producto `BR-T1`. Viven en la **raíz** del bucket público
`store-products`; en la base, `store_products.image_path` guarda el nombre
del archivo y el frontend arma la URL pública:

```
https://<proyecto>.supabase.co/storage/v1/object/public/store-products/br-t1.webp
```

## Reponerlas o agregar nuevas

El bucket no acepta escrituras anónimas, así que se sube por el panel de
Supabase (Storage → `store-products` → subir a la raíz) o por terminal con
la service key (Dashboard → Settings → API keys):

```bash
cd Biozin-Royale-Backend.API/Extras/store-images
export SUPABASE_URL=https://ldolvhdrbpstmplszqav.supabase.co
export SERVICE_KEY=...   # service_role / secret key, nunca la publicable
for f in br-*.webp; do
  curl -s -o /dev/null -w "%{http_code} $f\n" -X POST \
    "$SUPABASE_URL/storage/v1/object/store-products/$f" \
    -H "apikey: $SERVICE_KEY" -H "Authorization: Bearer $SERVICE_KEY" \
    -H "Content-Type: image/webp" -H "x-upsert: true" --data-binary @"$f"
done
```

Si a un producto le falta la imagen (o falla la carga), la app muestra su
dibujo vectorial, así que no se rompe nada.
