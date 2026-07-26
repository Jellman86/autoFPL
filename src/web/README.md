# Web boundary

The first Gameweek decision room is served as static HTML, CSS and JavaScript
from `src/backend/AutoFpl.Api/wwwroot` so the private home-lab deployment remains
one application and one container. It consumes typed JSON from the .NET API and
is non-authoritative.

When a qualifying official FPL capture exists, `/api/v1/advice/demo` joins its
real player identities, Premier League portrait URLs, clubs and next-Gameweek
fixtures to the deliberately synthetic forecast fixture. Without a capture it
falls back to synthetic identities. Both states keep every forecast value
explicitly labelled as synthetic.

The responsive decision room renders the XI and bench as portrait cards. A card
opens a persistent desktop dossier or a mobile sheet with the player's
cutoff-correct prior outcomes, next six Gameweeks, forecast placeholder,
explanation and risk. Player selection is reflected in the URL. Missing
portraits use initials, and missing history remains visibly missing rather than
being imputed in the browser.

Introduce a dedicated TypeScript build only when interaction complexity provides
a concrete benefit. Browser code never receives database, model-provider or
deployment secrets.
