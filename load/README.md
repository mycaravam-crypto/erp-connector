# Load test

A read-only powerwizz test of the API: dashboard, history and source-schema calls, 20 users for 60 s, signed in with
one shared account. It only issues GET requests after the login, so it changes no data.

```bash
docker compose -f docker-compose.dev.yml up -d
PW_PASS=alice123 powerwizz-cli run load/connector.json --password-env PW_PASS --out load/run --max-error-rate 0.01 --max-p95 500
docwizz load import . load/run-bericht.json && docwizz generate . --html   # numbers next to each endpoint in docs/api.md
```

Logins are rate-limited to 20 per minute per IP (`AuthEndpoints.LoginRateLimiterPolicyName`), and every virtual user
logs in from the same machine: above 20 users, ramp up over at least users / 20 minutes. Numbers from the dev stack
(Debug build under `dotnet watch`, test database) are not production figures.
