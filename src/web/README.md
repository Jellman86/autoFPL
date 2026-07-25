# Web boundary

The first Gameweek decision room is served as static HTML, CSS and JavaScript
from `src/backend/AutoFpl.Api/wwwroot` so the private home-lab deployment remains
one application and one container. It consumes typed JSON from the .NET API and
is non-authoritative.

The initial `/api/v1/advice/demo` response is deliberately synthetic. It proves
the formation, player-card, explanation, alternative and AI-access presentation
contract without presenting invented values as a fitted forecast.

Introduce a dedicated TypeScript build only when interaction complexity provides
a concrete benefit. Browser code never receives database, model-provider or
deployment secrets.
