# PostgreSQL Coupling Analysis

**Arbeitsauftrag 1 — technische Bestandsaufnahme.** Diese Analyse ist rein deskriptiv: sie
inventarisiert jede Stelle im Repository, an der Code, Konfiguration, Tests, Deployment oder
Dokumentation eine Annahme über PostgreSQL (statt eines generischen "ERP-Datenbank"-Konzepts)
treffen. Es wurden **keine** funktionalen Änderungen vorgenommen — nur Lektüre, Grep/Glob-Suche und
(am Ende) ein unveränderter Build/Test-Lauf.

Scope: `src/Connector.Api`, `src/Connector.Core`, `src/Connector.Infrastructure`,
`src/connector-ui`, `tests/*`, `docker-compose*.yml`, `Dockerfile`, `testdb/`, `README.md`,
`knowledge/**`.

---

## 1. Zusammenfassung

| Kategorie | Fundstellen |
|---|---:|
| Connection | 12 |
| Query Execution | 11 |
| SQL Dialect | 9 |
| Schema Discovery | 3 |
| Configuration | 4 |
| API | 4 |
| UI | 3 |
| Test | 9 |
| Deployment | 4 |
| Documentation | 4 |
| **Gesamt** | **63** |

Die Zahlen zählen Fundstellen (Datei + Methode/Bereich), nicht einzelne Code-Zeilen — ein Muster wie
`json_build_object` taucht z.B. an vier Stellen im selben Stil auf und wird als vier Fundstellen
geführt, weil jede eine eigene spätere Abstraktionsentscheidung berührt.

**Wichtigster Befund:** Das Repository trennt bereits sauber zwei Datenbanken:

1. **Die Application-/Log-DB** (`ExportLogDbContext`, SQLite, via EF Core) — hält Audit-Log,
   Settings, Export-/Import-Definitionen und -Runs. Diese Schicht ist **nicht** PostgreSQL-gekoppelt;
   EF Core abstrahiert sie bereits vollständig (austauschbar gegen jeden EF-Core-Provider).
2. **Die ERP-Quell-DB** (`ErpConnectionConfig` → `Npgsql`) — die tatsächliche externe
   Kunden-/ERP-Datenbank, gegen die exportiert/importiert wird. **Diese** Schicht ist durchgehend
   und absichtlich hart an PostgreSQL gekoppelt: rohe `NpgsqlConnection`/`NpgsqlCommand`, dialektspezifisches
   SQL (`json_build_object`, `json_agg`, `string_agg`, `::text`-Casts), `information_schema`-Introspektion
   und ein Frontend, das TLS-Modi 1:1 aus Npgsql's `SslMode`-Enum übernimmt.

Diese Kopplung ist an einer einzigen Stelle bewusst dokumentiert (nicht von mir hinzugefügt, bereits
im Repo vorhanden): `knowledge/pipeline/export-definitions-2.0.md` §8 nennt es explizit einen
DIP-Verstoß ("`DynamicExportService` builds a concrete `NpgsqlConnection` directly; introduce a
connection-provider abstraction only if per-export connection sourcing becomes a real need") und
verwirft eine Abstraktion bewusst als verfrüht (YAGNI). Diese Analyse bestätigt und detailliert diesen
bereits erkannten Zustand, ändert ihn aber nicht.

---

## 2. Connection

| # | Datei | Klasse/Methode | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| C1 | `src/Connector.Core/DynamicExport/ExportMappingTypes.cs:77` | `record ErpConnectionConfig` | Config-Record selbst dokumentiert als "ERP PostgreSQL connection parameters"; Felder (`Host`,`Port`,`Database`,`Username`,`Password`,`SslMode`) sind aber DB-agnostisch benannt | Niedrig (Datenstruktur ist bereits generisch genug) | Kein Handlungsbedarf am Shape selbst; ggf. XML-Doc entschärfen | Gering — reine Doku-Änderung möglich, ohne Codeeinfluss |
| C2 | `src/Connector.Infrastructure/DynamicExportService.cs:48-59` | `BuildConnectionString(ErpConnectionConfig)` | Baut direkt einen `NpgsqlConnectionStringBuilder`; einziger Ort, der `ErpConnectionConfig` in eine konkrete Provider-Connection-String übersetzt | **Hoch** — zentraler Single-Point, an dem jede Provider-Abstraktion ansetzen müsste | `IErpConnectionStringBuilder`/Factory-Interface, das pro DB-Provider eine Implementierung liefert | Mittel — von 9 Call-Sites verwendet (ExportWorker, ImportWorker, ExportDefinitionRunner, ImportRunReleaser, 3× Endpoints); Signaturänderung propagiert überall |
| C3 | `src/Connector.Infrastructure/DynamicExportService.cs:67-70` | `ParseSslMode(string?)` | Gibt `Npgsql.SslMode`-Enum zurück — Rückgabetyp ist Provider-spezifisch | **Hoch** | Eigenes DB-agnostisches `enum SslMode` in `Connector.Core`, das pro Provider gemappt wird | Mittel — SR-03-Fix hängt direkt daran; Verhalten (Fallback auf "Prefer") ist sicherheitsrelevant |
| C4 | `src/Connector.Api/Endpoints/ConnectionEndpoints.cs:141`, `:149` | `ConnectAndIntrospectAsync`, `IntrospectSchemaAsync` | Nimmt/öffnet `NpgsqlConnection` als Parametertyp | **Hoch** | Interface `IErpConnection`/`DbConnection` (ADO.NET-Basistyp) statt konkretem Npgsql-Typ | Mittel — Methode ist `internal`, aber von Tests (`ImportDefinitionEndpoints.Validation.cs`) wiederverwendet |
| C5 | `src/Connector.Infrastructure/ExportWorker.cs:141` | `RunExportAsync` | `new NpgsqlConnection(...)` direkt im Worker | **Hoch** | Über C2/C4-Abstraktion beziehen statt selbst instanziieren | Gering isoliert, aber Teil eines wiederkehrenden Musters (9× im Code dupliziert) |
| C6 | `src/Connector.Infrastructure/ImportWorker.cs:263` | `ProcessFileAsync` | `new NpgsqlConnection(...)` direkt im Worker | **Hoch** | s. C5 | s. C5 |
| C7 | `src/Connector.Infrastructure/ExportDefinitionRunner.cs:76` | `ExecuteAsync` | `new NpgsqlConnection(...)` | **Hoch** | s. C5 | s. C5 |
| C8 | `src/Connector.Infrastructure/ImportRunReleaser.cs:88` | `ReleaseAsync` | `new NpgsqlConnection(...)` + `BeginTransactionAsync` (ADO.NET-Transaktion) | **Hoch** | s. C5; Transaktionsverwaltung müsste hinter demselben Interface stehen | Mittel — Transaktions-/Rollback-Semantik ist korrektheitskritisch (four-eyes release) |
| C9 | `src/Connector.Api/Endpoints/ExportDefinitionEndpoints.cs:278` | Preview-Handler | `new NpgsqlConnection(...)` inline im Endpoint | **Hoch** | s. C5 | Gering |
| C10 | `src/Connector.Api/Endpoints/ImportDefinitionEndpoints.cs:281,400` | Preview-/Stage-Handler | `new NpgsqlConnection(...)` inline (2×) | **Hoch** | s. C5 | Gering |
| C11 | `src/Connector.Api/Endpoints/ImportDefinitionEndpoints.Validation.cs:128` | `ValidateRequestAsync` | `new NpgsqlConnection(...)` zur Schema-Introspektion bei Save-Time-Validierung | **Hoch** | s. C4 | Gering |
| C12 | `src/Connector.Api/Endpoints/PipelineEndpoints.cs:76,204,282` | Legacy `/api/pipeline/*` Handler | `new NpgsqlConnection(...)` inline (3×) | **Hoch** | s. C5 | Gering — Legacy-Pfad, laut Kommentar potenziell auslaufend |

**Beobachtung:** `BuildConnectionString` (C2) ist der einzige Ort, der `ErpConnectionConfig` in
Provider-Syntax übersetzt — das ist gut (ein Single Point of Truth). Das eigentliche
Kopplungsproblem ist nicht dort, sondern in den **9 Call-Sites (C5–C12)**, die alle direkt
`new NpgsqlConnection(...)` schreiben, statt eine Factory/Abstraktion zu injizieren. Eine künftige
Mehrdialekt-Unterstützung bräuchte zuerst eine Connection-Factory, die diese 9 Stellen ersetzt.

---

## 3. Query Execution

| # | Datei | Klasse/Methode | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| Q1 | `DynamicExportService.LegacyMapping.cs:73` | `ExecuteQueryAsync` | `NpgsqlCommand`/`NpgsqlDataReader`, `reader.GetDataTypeName(i)` prüft auf Postgres-Typnamen `"date"/"timestamp"/"timestamptz"` | **Hoch** | Query-Execution hinter `DbCommand`/`DbDataReader` (ADO.NET-Basistypen) kapseln; Typnamen-Vergleich durch `reader.GetFieldType(i) == typeof(DateTime)` ersetzen (provider-neutral) | Mittel — Datumsformatierung (`yyyy-MM-dd`) ist exportformatrelevant; falscher Typ-Name-Vergleich verändert stillschweigend Exportdaten |
| Q2 | `DynamicExportService.LegacyMapping.cs:202` | `ExecuteNestedJsonQueryAsync` | `NpgsqlCommand`; fängt `PostgresException` mit `SqlState == "21000"` explizit ab | **Hoch** | Fehlercode-Mapping hinter einer providerneutralen Exception-Übersetzungsschicht (`DbException` → Domain-Exception) | Mittel — SQLSTATE-Code ist eine Postgres-spezifische Fehlersemantik; Fehlermeldung an Endnutzer hängt daran |
| Q3 | `DynamicExportService.ExportNode.cs:82` | `ExecuteExportNodeQueryAsync` | Gleiches Muster wie Q2 (`NpgsqlConnection`, `PostgresException pex when pex.SqlState == "21000"`) | **Hoch** | s. Q2 | s. Q2 |
| Q4 | `DynamicExportService.ExportNode.cs:333` | `BuildExportNodeAsync` | Nimmt `NpgsqlConnection` als Parameter | Mittel | s. C4 | Gering |
| Q5 | `ImportNodeWalker.cs:31,273,388,457` | `WalkAsync`, `FetchRootRowAsync`, `ResolveChildAsync` | Durchgängig `NpgsqlConnection`/`NpgsqlCommand`/`NpgsqlDataReader` als Parametertypen; `CommandTimeout = 10` hartkodiert | Mittel | s. C4; Timeout in Konfiguration auslagern (unabhängig vom Provider-Thema, aber verwandt) | Mittel — Lesepfad für Import-Abgleich; Fehlverhalten hier führt zu falschen Diff-Ergebnissen |
| Q6 | `ImportRunReleaser.cs:88-119` | `ReleaseAsync` | `NpgsqlConnection` + `NpgsqlTransaction` + `NpgsqlCommand`; UPDATE mit `IS NOT DISTINCT FROM`-Guard (Postgres-Syntax, s. SQL Dialect) | **Hoch** (schreibender Pfad!) | s. C4/C8 | **Hoch** — einziger schreibender ERP-Zugriff im System; jede Änderung an Connection-/Transaktionshandling kann Datenintegrität der Kunden-ERP-DB gefährden |
| Q7 | `DynamicExportService.cs` (Aufrufer-seitig, z.B. `ExportWorker.cs:143`) | `BuildExportAsync(conn, ...)` | Übergibt konkrete `NpgsqlConnection` durch die gesamte Aufrufkette (Worker → Service → Query) | Mittel | Aufrufkette auf `DbConnection` umstellen (ADO.NET-Basistyp deckt bereits 90% ab) | Gering–Mittel |
| Q8 | `ImportDefinitionEndpoints.cs:278,286` (Preview) | Inline-Handler | `walkResult = await ImportNodeWalker.WalkAsync(conn, ...)` mit `NpgsqlConnection conn` | Mittel | s. C4 | Gering |
| Q9 | `ImportDefinitionEndpoints.cs:400,404` (Stage) | Inline-Handler | Gleiches Muster | Mittel | s. C4 | Gering |
| Q10 | `ExportDefinitionEndpoints.cs:283` | Preview-Handler | `ExecuteExportNodeQueryAsync(conn, ...)` | Mittel | s. C4 | Gering |
| Q11 | `PipelineEndpoints.cs:81,209,289,318` | Legacy `/api/pipeline/*` | `ExecuteQueryAsync`/`ExecuteNestedJsonQueryAsync`/`BuildExportAsync` mit `NpgsqlConnection` | Mittel | s. C4 | Gering |

**Beobachtung:** Die Query-Execution-Ebene ist bereits gut zentralisiert in
`DynamicExportService`/`ImportNodeWalker`/`ImportRunReleaser` — es gibt keine wild verstreute
Ad-hoc-SQL-Ausführung in den Endpoints selbst (die Endpoints öffnen nur die Connection und delegieren).
Das senkt den Umfang einer künftigen Abstraktion erheblich: eine Provider-Abstraktion müsste primär an
diesen ca. 6 Kernmethoden ansetzen, nicht an Dutzenden Stellen.

---

## 4. SQL Dialect

| # | Datei | Klasse/Methode | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| S1 | `DynamicExportService.ExportNode.cs:61,64,121` | `BuildExportNodeExpr`, `ExecuteExportNodeQueryAsync` | `json_build_object(...)`, `json_agg(...)`, `COALESCE(..., '[]'::json)` — native Postgres-JSON-Funktionen bilden die gesamte verschachtelte JSON-Baumstruktur direkt in SQL | **Sehr hoch** | Kapselung hinter einem `ISqlDialect`-Strategy-Objekt (`BuildObjectExpr`, `BuildArrayAggExpr`), das je Zieldatenbank (z.B. MySQL `JSON_OBJECT`/`JSON_ARRAYAGG`, MSSQL `FOR JSON`) eine eigene Implementierung liefert | **Sehr hoch** — das ist der Kern der gesamten Export-Engine (Phase 14 "ExportNode tree engine"); eine Änderung hier berührt jeden verschachtelten JSON-Export im System |
| S2 | `DynamicExportService.LegacyMapping.cs:100-101,184,189,229` | `ExecuteQueryAsync` (Relations), `BuildNestedGroupExpr`, `ExecuteNestedJsonQueryAsync` | `string_agg(...)`, `array_agg(...)`, `array_to_string(array_agg(...))`, `json_build_object`/`json_agg` — identisches Muster wie S1, aber im Legacy-Pfad (`/api/pipeline/*`) | **Sehr hoch** | s. S1 | **Sehr hoch** — zwei parallele Engines (Legacy + ExportNode) müssten beide migriert werden; laut Kommentar in `DynamicExportService.LegacyMapping.cs:9-12` ist unklar, ob/wann der Legacy-Pfad abgeschaltet wird |
| S3 | `DynamicExportService.cs:83` | `QI(string identifier)` | Doppelte-Anführungszeichen-Quoting (`"identifier"`) — Postgres-/SQL-Standard-Stil, aber z.B. inkompatibel mit MySQL (Backticks) | **Hoch** | `ISqlDialect.QuoteIdentifier(string)` | Mittel — von praktisch jeder SQL-Erzeugung im System verwendet (zentraler, aber einfacher Wechselpunkt) |
| S4 | `ImportNodeWalker.cs:285,436` | `FetchRootRowAsync`, `ResolveChildAsync` | `WHERE {col}::text = @val` — Postgres-Cast-Syntax `::text` zum typunabhängigen Vergleich | **Hoch** | `ISqlDialect.CastToText(column)` bzw. providerspezifische `CAST(col AS VARCHAR)` | Mittel — Matching-Logik für Importkorrelation hängt exakt an diesem Cast-Verhalten |
| S5 | `ImportRunReleaser.cs:98-106` | `ReleaseAsync` | `::text`-Cast im UPDATE-Guard, plus `IS NOT DISTINCT FROM` (Postgres/SQL-Standard-NULL-safe-Vergleich, in MySQL z.B. `<=>`) | **Sehr hoch** | s. S4; NULL-safe-Vergleich ebenfalls hinter Dialect-Strategy | **Hoch** — schreibender Pfad; NULL-Semantik-Fehler hier führen zu stillen Fehlkonflikten oder ungewollten Schreibvorgängen |
| S6 | `DynamicExportService.ExportNode.cs:53,103` | `BuildExportNodeExpr`, `ExecuteExportNodeQueryAsync` | `{col}::text` bei jedem Scalar-Feld (erzwingt Text-Repräsentation im JSON) | Mittel | s. S4 | Mittel — ändert Exportformat (Zahlen/Boolean würden sonst als native JSON-Typen statt Strings erscheinen) |
| S7 | `DynamicExportService.LegacyMapping.cs:100-101` | `ExecuteQueryAsync` | `::text` bei Relation-Aggregation vor `string_agg`/`array_agg` | Mittel | s. S4 | Gering–Mittel |
| S8 | `ExportDefinitionEndpoints.cs:494-501` | `DangerousFilterKeywordRegex` | Denylist enthält Postgres-spezifische Gefahrenfunktionen (`pg_sleep`, `pg_read_file`, `dblink`, `lo_import`/`lo_export`, `pg_catalog`, `pg_shadow`, `pg_authid`) — Sicherheitsfilter ist an Postgres-Funktionsnamen gebunden | **Hoch** (sicherheitsrelevant) | Bei Multi-Dialekt-Unterstützung müsste die Denylist pro Dialekt erweitert werden (z.B. MySQL `LOAD_FILE`, MSSQL `xp_cmdshell` — Letzteres ist bereits mit aufgenommen) | **Hoch** — dies ist eine Sicherheitskontrolle (SR-01); jede Änderung muss von einem Security-Review begleitet werden, nicht nur von einem Refactoring |
| S9 | `ConnectionEndpoints.cs:161-190` | `IntrospectSchemaAsync` | `LEFT JOIN LATERAL (...)` — Postgres-spezifische Lateral-Join-Syntax für die FK-Auflösung | **Hoch** | Siehe Abschnitt „Schema Discovery" | Mittel |

**Beobachtung:** Dies ist der Kern der Kopplung und zugleich der Kern des Produkt-Nutzenversprechens.
Die native JSON-Aggregation in SQL (`json_build_object`/`json_agg`) ist eine bewusste
Performance-/Einfachheits-Entscheidung (ein Query statt N+1 Objektaufbau in C#) — jede Abstraktion
müsste diesen Vorteil für einen zweiten Dialekt neu erkaufen, nicht nur die Syntax austauschen.

---

## 5. Schema Discovery

| # | Datei | Klasse/Methode | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| D1 | `ConnectionEndpoints.cs:148-221` | `IntrospectSchemaAsync` | Nutzt `information_schema.columns`, `information_schema.table_constraints`, `information_schema.key_column_usage`, `information_schema.constraint_column_usage` + `LEFT JOIN LATERAL` zur PK/FK-Erkennung; liest `is_identity`/`is_generated` (Postgres-spezifische ANSI-Erweiterungen) | **Sehr hoch** | `ISchemaIntrospector`-Interface mit provider-spezifischer Implementierung (z.B. `INFORMATION_SCHEMA` existiert auch in MySQL/MSSQL, aber `is_generated`/`LATERAL`/FK-Auflösung unterscheiden sich strukturell) | **Sehr hoch** — dies ist die Grundlage für: Verbindungstest (Step 1 UI), AllowedWritableColumns-Validierung (Import-Definitionen, Open Decision #9), und die komplette Schema-Anzeige im Frontend |
| D2 | `Dtos.cs:64` (Kommentar) | `SourceColumnDto` | Dokumentiert explizit: "Populated by ConnectionEndpoints.IntrospectSchemaAsync from information_schema.columns" | Niedrig (Doku) | — | Gering |
| D3 | `ConnectionEndpoints.cs:224-304` | `DemoSourceSchema()` | Hartkodiertes Demo-Schema mit Postgres-typischen Typnamen (`"character varying(100)"`, `"uuid"`, `"date"`) als Fallback, wenn keine echte Verbindung konfiguriert ist | Mittel | Demo-Schema-Typnamen sind reine Anzeigedaten — bei Multi-Dialekt-Support müsste hier ein dialektneutrales oder pro-Dialekt-Demo-Schema stehen | Gering — betrifft nur die UI-Voransicht vor der ersten echten Verbindung |

**Beobachtung:** Schema Discovery ist die am tiefsten in Postgres-DDL-Introspektion verwurzelte
Komponente im System (`LEFT JOIN LATERAL` ist keine ANSI-SQL-Standardsyntax). Sie ist gleichzeitig
funktional zentral — praktisch jede spätere Validierung (Import-Spaltenwhitelist,
Fremdschlüssel-Erkennung im Export-Baum-Editor) hängt an ihrem Ergebnis (`SourceTableDto[]`), dessen
**Shape** selbst bereits provider-neutral ist. Eine Abstraktion müsste nur die SQL-Erzeugung
ersetzen, nicht das DTO.

---

## 6. Configuration

| # | Datei | Klasse/Methode | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| CF1 | `Connector.Infrastructure/Connector.Infrastructure.csproj:12`, `Connector.Api/Connector.Api.csproj:14`, `tests/Connector.Integration.Tests/Connector.Integration.Tests.csproj:13` | `<PackageReference Include="Npgsql" Version="10.0.3" />` | Harte NuGet-Abhängigkeit auf den Npgsql-Treiber in **drei** Projekten (Api, Infrastructure, Tests) | **Hoch** | Nur in einer künftigen "Connector.Erp.Postgres"-Adapterschicht referenzieren; Core/Api sollten nur gegen ein Abstraktions-Interface kompilieren | Mittel — Paketreferenz-Umbau ist mechanisch, aber berührt jedes der drei `.csproj` |
| CF2 | `Connector.Api/Program.cs:197-198` | `AddDbContext<ExportLogDbContext>` | `opt.UseSqlite(...)` — dies ist die **App-eigene** Log-DB, **nicht** die ERP-Quelle; bereits providerneutral über EF Core | Niedrig | Kein Handlungsbedarf (bereits abstrahiert) | Gering |
| CF3 | `Connector.Api/appsettings.Development.json:14-15`, `appsettings.Production.json:28-30` | `ConnectionStrings:ExportLog` | SQLite-Connection-String für die App-DB — **nicht** PostgreSQL-bezogen; für die ERP-Verbindung existiert **kein** vergleichbarer statischer Config-Eintrag, da `ErpConnectionConfig` zur Laufzeit in der App-DB (`AppSettings`-Tabelle) gespeichert wird | Niedrig (Feststellung, keine Kopplung) | — | — |
| CF4 | `ConnectionEndpoints.cs:17-21` | `BlockedNetworks` (SSRF-Schutz) | Kommentar/Logik ist DB-agnostisch (blockt nur Cloud-Metadata-IP-Ranges), aber der umgebende Endpoint-Vertrag (`ErpConnectionConfig` mit `SslMode` aus Npgsql-Namen) ist Postgres-spezifisch | Mittel | s. C3 | Gering — Blockliste selbst ist unabhängig vom DB-Typ |

**Beobachtung:** Die Konfigurationsebene zeigt ein sauberes Architekturmuster: Es gibt **zwei völlig
getrennte** Connection-String-Konzepte — ein statisches (`appsettings.json` → SQLite/App-DB, bereits
abstrahiert) und ein dynamisches, laufzeit-persistiertes (`AppSettings`-Tabelle →
`ErpConnectionConfig` → Postgres, hart gekoppelt). Jede künftige Multi-Dialekt-Unterstützung müsste
`ErpConnectionConfig` um ein Diskriminator-Feld (z.B. `Provider: "Postgres"|"MySql"`) erweitern.

---

## 7. API

| # | Datei | Klasse/Methode | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| A1 | `ConnectionEndpoints.cs:27-28` | `IsValidSslMode(string?)` | Validiert Eingabe direkt gegen `Enum.TryParse<Npgsql.SslMode>` — API-Vertrag akzeptiert nur Npgsql-Enum-Namen | **Hoch** | Eigenes API-DTO-Enum, das serverseitig auf den jeweiligen Provider gemappt wird | Mittel — Breaking Change für jeden bestehenden API-Client, der aktuelle SslMode-Strings sendet |
| A2 | `ConnectionEndpoints.cs:84-86` | POST `/api/connection` Handler | Fehlermeldung nennt Npgsql-SslMode-Namen wörtlich ("SslMode must be one of: Disable, Allow, Prefer, Require, VerifyCA, VerifyFull.") | Mittel | s. A1 | Gering |
| A3 | `ExportDefinitionEndpoints.cs:494-501` | Filter-Validierung | Öffentlich sichtbare Fehlermeldungen/Verhalten der Filter-Validierung sind implizit an Postgres-Funktionsnamen gebunden (s. S8) — API-Vertrag "was ist ein sicherer Filter" ist dialektspezifisch | **Hoch** (Sicherheitsgrenze) | s. S8 | Hoch (sicherheitsrelevant) |
| A4 | `Dtos.cs:97` | `record ErpConnectionInfo` | Response-DTO von GET `/api/connection` exponiert `SslMode` 1:1 als Postgres-Enum-String an das Frontend | Mittel | s. A1 | Mittel — Frontend-Dropdown (s. UI-Abschnitt) müsste synchron geändert werden |

---

## 8. UI

| # | Datei | Klasse/Methode | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| U1 | `connector-ui/src/views/ConnectionView.vue:22` | `const port = ref('5432')` | Hartkodierter Default-Port 5432 (Postgres-Standardport) | Niedrig | Placeholder/Default aus Backend-Metadaten oder Config beziehen | Gering — reiner UX-Default, kein funktionales Risiko |
| U2 | `connector-ui/src/views/ConnectionView.vue:153-165` | SSL-Mode `<Select>` | Dropdown-Optionen sind wörtlich Npgsql's `SslMode`-Namen (`Disable/Allow/Require/VerifyCA/VerifyFull`); Hilfetext beschreibt Postgres-spezifisches TLS-Fallback-Verhalten | **Hoch** | Eigene UI-Enum-Werte, die nur im Backend (A1) auf den jeweiligen Provider gemappt werden | Mittel — direkt sichtbar für Endnutzer; jede Änderung ist eine UX-Änderung, nicht nur intern |
| U3 | `connector-ui/src/views/ConnectionView.vue:104-137` | Hilfetext/Alerts | Text nennt "PostgreSQL" explizit mehrfach ("This is the PostgreSQL database behind your ERP system…", "Enter the connection details for the PostgreSQL database…", Docker-Hostname-Hinweis `testdb`) | Mittel | Textbausteine dialektneutral formulieren oder dynamisch aus Backend-Capability ableiten | Gering — reiner Text, aber hohe Sichtbarkeit für Nutzer/Support |

**Beobachtung:** Das Frontend trifft keine strukturellen Postgres-Annahmen (kein Postgres-spezifischer
Code, nur Text/Defaults/Enum-Werte) — die Kopplung ist hier rein oberflächlich und ließe sich mit
überschaubarem Aufwand entkoppeln, sofern das Backend (A1/A4) zuerst entkoppelt wird.

---

## 9. Test

| # | Datei | Klasse/Methode | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| T1 | `tests/Connector.Integration.Tests/ErpTestFixture.cs:14-23` | `ConnectionString`, `Config` | Hartkodierte Postgres-Connection-String-Konstante (`Host=localhost;Port=5432;...`) + `ErpConnectionConfig`, geteilt von 7 Testklassen | **Hoch** | Bereits als gemeinsame Fixture konsolidiert (s. `code-health-backlog.md` #3 — "done"); künftige Abstraktion müsste hier ansetzen, um Tests gegen einen zweiten Dialekt laufen zu lassen | Mittel — von 7 Testdateien genutzt; Änderung hier propagiert in den gesamten Postgres-Testsuite-Teil |
| T2 | `ErpTestFixture.cs:25-43` | `TryOpenAsync`, `IsAvailableAsync` | "No-op instead of fail"-Muster: Tests überspringen sich selbst (`return` ohne Assertion), wenn `testdb` nicht erreichbar ist, statt echtes Skip (xunit 2.9.2 hat kein `Assert.Skip`) | **Hoch** (Testlücke!) | Nach xunit-Upgrade (≥2.9.3 oder v3) auf echtes `Skip` umstellen, damit übersprungene Postgres-Tests in der CI-Ausgabe sichtbar sind statt als "grün" durchzulaufen | Mittel — falsches Sicherheitsgefühl: ein CI-Lauf ohne laufende `testdb` zeigt aktuell keine offensichtliche Warnung |
| T3 | `tests/Connector.Integration.Tests/*Postgres*.cs` (7 Dateien: `DynamicExportServiceFlatQueryPostgresTests`, `DynamicExportServiceNestedJsonPostgresTests`, `ExportNodeQueryPostgresTests`, `ImportDefinitionEndpointsPostgresTests`, `ImportNodeWalkerPostgresTests`, `ImportRunReleaserPostgresTests`, `ImportWorkerPostgresTests`) | — | Jede Datei fordert eine **echte, laufende Postgres-Instanz** (`docker-compose --profile test up -d testdb`); kein Testcontainers/kein In-Memory-Ersatz | **Sehr hoch** | Mittelfristig: Testcontainers-basierte, automatisch verwaltete Postgres-Instanz statt manuell gestarteter `docker-compose`-Fixture (reduziert Flakiness, kein manueller Schritt); langfristig: Provider-agnostische Test-Suite über `ISqlDialect`-Abstraktion | Hoch bei Testinfrastruktur-Änderung — 7 Dateien mit vermutlich hunderten Testfällen hängen direkt an dieser Fixture |
| T4 | `tests/Connector.Integration.Tests/DynamicExportServiceTests.cs:106,119,144` | Unit-Tests für `BuildConnectionString` | Verwendet `Npgsql.NpgsqlConnectionStringBuilder` direkt zum **Parsen** des Testergebnisses (Assertion), nicht nur zum Erzeugen | Mittel | Bleibt zwangsläufig Postgres-spezifisch, solange C2 nicht abstrahiert ist | Gering |
| T5 | `tests/Connector.Integration.Tests/ExportNodeEngineTests.cs:369` | Test für Verbindungsfehlerpfad | `new NpgsqlConnection("Host=unused;Timeout=1")` — Verwendet echten Npgsql-Typ auch für einen reinen "nie tatsächlich verbindenden" Negativtest | Niedrig | Könnte mit einem `DbConnection`-Fake statt echtem Npgsql-Objekt arbeiten | Gering |
| T6 | `tests/Connector.Integration.Tests/ConnectionEndpointsSslModeValidationTests.cs` | gesamte Datei | Testet `IsValidSslMode` gegen Npgsql-`SslMode`-Enum-Werte | Mittel | Folgt A1 | Gering |
| T7 | `tests/Connector.Integration.Tests/ConnectionEndpointsHostValidationTests.cs` | gesamte Datei | Testet SSRF-Blockliste (`BlockedNetworks`) — funktional DB-agnostisch, aber im Kontext eines Postgres-Connection-Endpoints | Niedrig | — | Gering |
| T8 | `tests/Connector.Integration.Tests/ImportRunReleaserPostgresTests.cs:28,36-38` | `ReadStatusAsync`, `ResetStatusAsync` | Rohes `NpgsqlCommand` mit `id::text = @id` (Postgres-Cast) direkt im Test-Helper, dupliziert das Produktionsmuster aus S4/S5 | Mittel | s. S4 | Gering (Testcode) |
| T9 | `tests/Connector.Integration.Tests/Connector.Integration.Tests.csproj:13,28-32` | Projektdatei | Direkte `Npgsql`-Paketreferenz **und** `InternalsVisibleTo`-Zugriff auf `Connector.Api`, explizit begründet mit "exercises the internal, schema-aware AllowedWritableColumns validator directly against a real Postgres schema" | Mittel | Dokumentiert bereits bewusst als Kompromiss (Kommentar im csproj) — kein akuter Handlungsbedarf | Gering |

**Testlücken (siehe auch Abschnitt 12):**
- Kein automatisierter Nachweis, dass die Postgres-Tests bei fehlender `testdb` tatsächlich als
  "übersprungen" statt "grün, aber leer" erkennbar sind (T2).
- Keine Tests gegen einen zweiten SQL-Dialekt existieren oder könnten aktuell existieren — jede
  Aussage über "wie schwer wäre ein zweiter Dialekt" ist unverifiziert, weil nichts das prüft.
- `DemoSourceSchema()` (D3) hat keinen Test, der sicherstellt, dass sein Shape mit dem echten
  `IntrospectSchemaAsync`-Ergebnis synchron bleibt (Drift-Risiko zwischen Demo- und Realdaten-Schema).

---

## 10. Deployment

| # | Datei | Klasse/Methode | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| DP1 | `docker-compose.yml:58-80` | Service `testdb` | `image: postgres:16-alpine` als optionales Test-/Lokal-Profil (`profiles: ["test"]`) — explizit als "NOT part of the production stack" kommentiert; Produktionsverbindung zeigt auf eine externe, vom Betreiber bereitgestellte Postgres-Instanz | Mittel | Kein Handlungsbedarf für Produktion (Postgres ist hier die *Kunden*-ERP-DB, kein internes Implementierungsdetail); ggf. Compose-Profile um einen zweiten DB-Typ erweiterbar machen | Gering |
| DP2 | `docker-compose.dev.yml:61-79` | Service `testdb` | Gleiches Muster wie DP1, dupliziert für den Hot-Reload-Dev-Stack (laut Kommentar bewusst dupliziert, nicht geteilt) | Mittel | — | Gering |
| DP3 | `testdb/init.sql:1-58` | Seed-Schema | Postgres-spezifisches DDL: `CREATE EXTENSION pgcrypto`, `gen_random_uuid()`, `GENERATED ALWAYS AS (...) STORED` — bildet exakt das Demo-/Test-Schema nach, das `DemoSourceSchema()` (D3) hartkodiert erwartet | Mittel | Bei Multi-Dialekt-Testsupport bräuchte jeder Dialekt ein äquivalentes Seed-Skript | Gering — reine Testinfrastruktur |
| DP4 | `docker-compose.yml:37-41` | Healthcheck `api` | `wget -qO- http://localhost:8080/api/health` — prüft **nicht** die ERP-Postgres-Erreichbarkeit, nur `log_db` (SQLite) und `staging`-Verzeichnis (s. Abschnitt 11) | **Hoch** (funktionale Lücke, keine reine Kopplung) | `/api/health` um einen optionalen ERP-Konnektivitätscheck erweitern | Mittel — Health-Status kann "healthy" melden, obwohl die eigentliche ERP-Quelle nicht erreichbar ist |

---

## 11. Documentation

| # | Datei | Bereich | Art der Kopplung | Kritikalität | Empfohlene Abstraktion | Risiko bei Änderung |
|---|---|---|---|---|---|---|
| DOC1 | `README.md:20,64,90,96,127,167,237,260` | Architekturübersicht, Quickstart, API-Tabelle | Nennt PostgreSQL an 8 Stellen explizit als die ERP-Quelldatenbank | Niedrig | Bei Multi-Dialekt-Support müsste README einen "unterstützte Datenbanken"-Abschnitt bekommen | Gering |
| DOC2 | `knowledge/pipeline/export-definitions-2.0.md:215-216` | §8 SOLID-Analyse | **Bereits vorhandene** explizite DIP-Anmerkung: "`DynamicExportService` builds a concrete `NpgsqlConnection` directly; introduce a connection-provider abstraction only if per-export connection sourcing becomes a real need" | — (Dokumentation der Kopplung selbst, keine neue Kopplung) | Bestätigt die YAGNI-Entscheidung dieser Analyse — sollte bei jeder künftigen Abstraktionsentscheidung als Ausgangspunkt zitiert werden | — |
| DOC3 | `knowledge/pipeline/import-definitions.md:392` | Architekturbeschreibung | Erwähnt `NpgsqlConnection`/Transaktion als "not a concrete provider" — beschreibt den *Wunschzustand* für den Import-Commit-Schritt, der im tatsächlichen Code (Q6/C8) noch nicht erreicht ist | Niedrig (Dokumentation eines Zielzustands) | Diese Doku-Aussage sollte mit dem tatsächlichen Code (`ImportRunReleaser.cs`) abgeglichen werden, sobald eine Abstraktion umgesetzt wird | Gering |
| DOC4 | `knowledge/planning/code-health-backlog.md:224-238` | "Duplicated Postgres connect-and-introspect logic" (Punkt 2) und Test-Fixture-Dopplung (Punkt 3, "done") | Dokumentiert bereits behobene Duplizierung der Postgres-Test-Fixtures über 6-9 Dateien hinweg | — (historisch, bereits erledigt) | Guter Präzedenzfall/Vorlage für eine künftige Konsolidierung der Produktionscode-Duplizierung (C5–C12) | — |

---

## 12. Architektur-Hotspots (Priorisierung)

Reihenfolge nach **Blast Radius × Kritikalität**, nicht nach Aufwand:

1. **SQL-Dialekt-Erzeugung (S1/S2 — `json_build_object`/`json_agg`/`string_agg`/`array_agg`).**
   Das ist der funktionale Kern beider Export-Engines (Legacy + ExportNode). Jede
   Mehrdialekt-Strategie beginnt hier, weil dies der Ort ist, an dem SQL-Text literal
   zusammengesetzt wird.
2. **Schema Discovery (D1 — `information_schema` + `LEFT JOIN LATERAL`).** Zweithöchster
   Blast Radius, weil UI-Verbindungstest, Export-Baum-Editor und Import-Spalten-Validierung
   alle von `SourceTableDto[]` abhängen, dessen Erzeugung Postgres-spezifisches DDL-Wissen
   voraussetzt.
3. **Connection-Instanziierung an 9 Call-Sites (C5–C12).** Kein einzelner Hotspot, aber die
   Summe aus wiederholtem `new NpgsqlConnection(...)` ist der Grund, warum eine Abstraktion
   heute noch keinen einzigen Injection-Point hat.
4. **Schreibender Import-Commit-Pfad (Q6/S5 — `ImportRunReleaser.ReleaseAsync`).** Kleinster
   Umfang der vier Hotspots, aber die höchste Einzelrisikoklasse, weil hier tatsächlich in die
   Kunden-ERP-Datenbank geschrieben wird (`IS NOT DISTINCT FROM`-Guard, Transaktionssemantik).
5. **Test-Fixture-Abhängigkeit von einer echten Postgres-Instanz (T1–T3).** Kein
   Produktionscode-Hotspot, aber jede Abstraktionsarbeit an 1–4 ist ohne eine entsprechend
   erweiterte Testinfrastruktur nicht verifizierbar.

## 13. Empfehlung: Was zuerst abstrahiert werden sollte

Falls (nicht Gegenstand dieses Auftrags, aber als Ausblick) eine Abstraktion beschlossen wird, ergibt
sich aus obiger Priorisierung folgende Reihenfolge geringsten Risikos bei größtem Nutzen:

1. **Connection-Factory zuerst** (C2 + C5–C12 konsolidieren): schafft einen einzigen
   Injection-Point, ohne SQL-Semantik zu verändern — geringstes Risiko, größter struktureller
   Hebel für alles Weitere.
2. **`ISqlDialect.QuoteIdentifier`/`CastToText`** (S3–S7): kleine, mechanische Kapselung mit
   überschaubarem Testaufwand.
3. **Schema-Introspektion** (D1) erst danach, weil sie fachlich komplexer ist (FK-Auflösung,
   `is_generated`) und von der Connection-Factory bereits profitieren würde.
4. **JSON-Aggregation** (S1/S2) zuletzt — größter Aufwand, weil hier tatsächlich unterschiedliche
   Zieldialekte grundverschiedene Fähigkeiten haben (nicht jede DB kann serverseitig verschachteltes
   JSON aggregieren).

## 14. Was zunächst unverändert bleiben kann

- **`ExportLogDbContext` (SQLite/EF Core)** — bereits vollständig providerneutral, keine
  PostgreSQL-Kopplung vorhanden (CF2/CF3).
- **UI-Textbausteine (U3)** — hohe Sichtbarkeit, aber kein struktureller Zwang; können unverändert
  bleiben, bis das Backend tatsächlich einen zweiten Dialekt unterstützt (sonst entsteht eine
  UI, die mehr verspricht als das Backend hält).
- **`ErpConnectionConfig`-Shape selbst (C1)** — die Feldnamen sind bereits DB-agnostisch; nur die
  XML-Dokumentation nennt Postgres explizit. Umbenennen lohnt sich erst mit einer echten zweiten
  Implementierung.
- **Docker/Compose-Postgres-Fixtures (DP1–DP3)** — Postgres ist hier korrekt die *reale
  Ziel-ERP-Datenbank* der meisten Kunden, kein austauschbares internes Implementierungsdetail; das
  ist kein technisches Schuldenproblem, sondern eine Produktentscheidung.
- **Security-Denylist (S8)** — bewusst konservativ und Postgres-spezifisch scharf formuliert
  (SR-01); jede Lockerung zugunsten von "Dialekt-Neutralität" würde die Sicherheitsgrenze schwächen
  und ist nicht ohne Security-Review zu ändern.

## 15. Testlücken (Zusammenfassung)

1. **Kein sichtbares Skip-Signal** (T2): Postgres-Tests laufen bei fehlender `testdb` still durch,
   ohne als "übersprungen" markiert zu werden (xunit-2.9.2-Limitierung) — ein stiller
   Nichttest-Zustand kann in CI unbemerkt bleiben.
2. **Kein Health-Check der ERP-Verbindung** (DP4): `/api/health` prüft nur die interne SQLite-DB
   und das Staging-Verzeichnis, nicht die tatsächliche Postgres-ERP-Erreichbarkeit — ein Operator
   könnte "healthy" sehen, während der nächtliche Export wegen ERP-Nichterreichbarkeit fehlschlägt.
3. **Keine Dialekt-Diversität in Tests**: Es existiert keine einzige Testklasse, die Verhalten gegen
   einen zweiten SQL-Dialekt verifiziert — jede künftige Abstraktionsarbeit beginnt bei
   Test-Coverage null für "Dialekt B".
4. **Kein Drift-Test zwischen Demo-Schema und Testdb-Schema** (D3/DP3): `DemoSourceSchema()` und
   `testdb/init.sql` müssen von Hand synchron gehalten werden; kein Test schlägt fehl, wenn sie
   auseinanderlaufen.
5. **Testcontainers fehlt**: Die Postgres-Testsuite verlangt eine manuell gestartete
   `docker-compose`-Instanz statt einer pro Testlauf isolierten, automatisch verwalteten Instanz —
   erhöht Flakiness-Risiko (Zustand zwischen Testläufen kann sich unbemerkt ansammeln, da
   `testdb-data` in `docker-compose.dev.yml` sogar persistent gemountet ist).

---

## 16. Build- und Testlauf (Verifikation, keine Codeänderung)

Ausgeführt in der Analyseumgebung (keine laufende `testdb`-Postgres-Instanz, kein Docker-Daemon
verfügbar):

```
dotnet restore Connector.sln   → OK (5/5 Projekte)
dotnet build Connector.sln     → Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test Connector.sln      → Connector.Core.Tests:         47 Passed, 0 Failed, 0 Skipped
                                  Connector.Integration.Tests: 348 Passed, 0 Failed, 0 Skipped
```

**Das bestätigt Testlücke T2 live:** Ohne erreichbare Postgres-Instanz (Port 5432 unerreichbar,
verifiziert) laufen alle sieben `*PostgresTests.cs`-Dateien trotzdem grün durch — als **"Passed"**,
nicht als **"Skipped"**. Der Grund ist exakt das in Abschnitt 9 (T2) beschriebene
"no-op-instead-of-fail"-Muster in `ErpTestFixture.TryOpenAsync`: jeder Testfall, der keine Verbindung
bekommt, kehrt kommentarlos ohne Assertion zurück und zählt als bestanden. Dieser Lauf beweist damit
in der Praxis, dass ein CI-Durchlauf ohne laufende `testdb` **keinerlei sichtbares Signal** gibt,
dass der überwiegende Teil der Postgres-Kopplung (Connection, Query Execution, SQL Dialect, Schema
Discovery) tatsächlich ungetestet blieb.

(Umgebungshinweis: `dotnet` war in der Sandbox nicht vorinstalliert; `dotnet-sdk-10.0` wurde per
`apt-get` installiert, da `dotnet-sdk-9.0` im verfügbaren Ubuntu-Repository fehlt. Kompiliert wird
weiterhin exakt gegen `net9.0` wie in den `.csproj`-Dateien deklariert — NuGet restauriert dafür die
passenden `net9.0`-Referenzpakete. Da nur die .NET-9.0-Runtime selbst fehlte, nicht das SDK, wurde für
den Testlauf `DOTNET_ROLL_FORWARD=LatestMajor` gesetzt, damit der Testhost die vorhandene
10.0-Runtime nutzt; das ändert nur, welche Runtime die bereits kompilierten net9.0-Assemblies
ausführt, nicht die Kompilierung selbst. Produktivcode wurde dabei nicht verändert.)

---

## 17. Methodik

Diese Analyse wurde erstellt durch:
- Volltextsuche (`grep`) nach den im Auftrag genannten Mustern (`Npgsql*`, `json_build_object`,
  `json_agg`, `string_agg`, `array_agg`, `::text`/`::json`, `information_schema`) über das gesamte
  Repository.
- Vollständiges Lesen jeder Datei mit Treffern in `src/Connector.Api`, `src/Connector.Core`,
  `src/Connector.Infrastructure`.
- Gezielte Prüfung von Frontend-Connection-Code (`connector-ui/src/api/connection.ts`,
  `ConnectionView.vue`), Docker-/Compose-Dateien, `testdb/init.sql` und README/knowledge-Dokumenten.
- Es wurde **keine** Datei verändert, außer der Neuanlage dieses Analyse-Dokuments.
