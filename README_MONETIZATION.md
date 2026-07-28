# Guía de Monetización de RefereeStats (Pay-Per-Event)

Este scraper soporta el modelo de monetización **Pay-Per-Event (PPE)** de Apify, lo que significa que puedes cobrar a los usuarios por la cantidad de datos exactos que extraen, en lugar de cobrar una tarifa plana mensual o cobrar por tiempo de cómputo.

## Eventos Implementados

El código emite automáticamente los siguientes eventos al API de Apify:

1. **`actor-start`**
   - Emitido una única vez cuando el Actor arranca.
   - Ideal para cubrir un coste base por ejecución.

2. **`profile-item`**
   - Emitido cada vez que se scrapea con éxito un perfil agregado de un árbitro (modo `profile`).

3. **`match-detail-item`**
   - Emitido cada vez que se scrapea un reporte granular de un partido (modo `matches`).

4. **`analytics-item`**
   - Emitido por cada reporte analítico generado (modo `analytics`).

## Cómo Configurar los Precios en Apify

Para que estos eventos generen ingresos reales, debes configurar su precio en la consola de Apify antes de publicar comercialmente tu Actor.

1. Ve a **Apify Console > Actors** y selecciona tu Actor (`referee-stats-scraper`).
2. En el menú de la izquierda, entra en **Monetization**.
3. Cambia el modelo de precios a **Pay per event**.
4. En la tabla de eventos, añade cada uno de los eventos exactamente con los nombres mencionados arriba.
5. Asigna el precio. Por ejemplo:

| Event Name          | Suggested Price (USD) | Justificación |
|---------------------|-----------------------|---------------|
| `actor-start`       | $0.05                 | Tarifa plana de inicio para disuadir micro-ejecuciones vacías. |
| `profile-item`      | $0.005                | Tarifa por cada perfil agregado extraído. |
| `match-detail-item` | $0.01                 | Coste superior porque cada partido requiere su propia petición HTTP a Transfermarkt. |
| `analytics-item`    | $0.002                | Menor coste porque es un cálculo puramente local (derivado de L2). |

6. Guarda los cambios. A partir de ahora, cada vez que un usuario ejecute tu Actor desde la Store, se le cobrará según el volumen extraído sumando los eventos registrados por esta integración.
