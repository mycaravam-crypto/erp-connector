-- Seed data for the local/test ERP Postgres fixture (docker-compose --profile test).
-- Mirrors the shape of the connector's hardcoded demo schema (see
-- ConnectionEndpoints.DemoSourceSchema) so Step 1 introspection has real tables to find.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE masterdata (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    article_name character varying(200),
    part_number character varying(100),
    manufacturer character varying(100)
);

CREATE TABLE systemconfiguration (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    serial character varying(100),
    article_id uuid REFERENCES masterdata(id),
    status character varying(50),
    commission_date date,
    technician_name character varying(100),
    storage_location character varying(200)
);

CREATE TABLE maintenance_plan (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    system_configuration_id uuid NOT NULL REFERENCES systemconfiguration(id),
    status character varying(50) NOT NULL,
    allocation_chart_ref character varying(100)
);

CREATE TABLE articlestructure (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    parent_id uuid REFERENCES masterdata(id),
    child_id uuid REFERENCES masterdata(id)
);

INSERT INTO masterdata (id, article_name, part_number, manufacturer) VALUES
    ('11111111-1111-1111-1111-111111111111', 'Compressor Unit CU-200', 'CU-200', 'Acme Industrial'),
    ('22222222-2222-2222-2222-222222222222', 'Control Valve CV-45', 'CV-45', 'Acme Industrial'),
    ('33333333-3333-3333-3333-333333333333', 'Sensor Array SA-10', 'SA-10', 'Northbridge Sensors');

INSERT INTO systemconfiguration (id, serial, article_id, status, commission_date, technician_name, storage_location) VALUES
    ('44444444-4444-4444-4444-444444444444', 'SN-00042', '11111111-1111-1111-1111-111111111111', 'active', '2024-03-15', 'J. Alvarez', 'Bay 3'),
    ('55555555-5555-5555-5555-555555555555', 'SN-00043', '22222222-2222-2222-2222-222222222222', 'active', '2024-05-02', 'M. Chen', 'Bay 7'),
    ('66666666-6666-6666-6666-666666666666', 'SN-00044', '11111111-1111-1111-1111-111111111111', 'decommissioned', '2022-01-10', 'J. Alvarez', 'Storage');

INSERT INTO maintenance_plan (id, system_configuration_id, status, allocation_chart_ref) VALUES
    ('77777777-7777-7777-7777-777777777777', '44444444-4444-4444-4444-444444444444', 'scheduled', 'AC-2024-011'),
    ('88888888-8888-8888-8888-888888888888', '55555555-5555-5555-5555-555555555555', 'scheduled', 'AC-2024-012');

INSERT INTO articlestructure (id, parent_id, child_id) VALUES
    ('99999999-9999-9999-9999-999999999999', '11111111-1111-1111-1111-111111111111', '33333333-3333-3333-3333-333333333333');
