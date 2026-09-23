-- MariaDB counterpart of the export_* fixture tables in init.sql (Arbeitsauftrag 7): the same tables, columns
-- and rows, in MariaDB types, so one ExportDefinition can be run against both backends and compared
-- (MariaDbExportParityTests). Keep the two in sync. Loaded into erp_testdb by CI and by the local
-- `docker-compose --profile test up -d testdb-mariadb` fixture. Read-only for every test.
CREATE TABLE export_customer (
    id INT PRIMARY KEY,
    name VARCHAR(100),
    vip BOOLEAN
) ENGINE = InnoDB;

CREATE TABLE export_order (
    id INT PRIMARY KEY,
    customer_id INT,
    placed_on DATE,
    total DECIMAL(10, 2),
    note TEXT,
    CONSTRAINT fk_export_order_customer FOREIGN KEY (customer_id) REFERENCES export_customer (id)
) ENGINE = InnoDB;

CREATE TABLE export_order_line (
    id INT PRIMARY KEY,
    order_id INT NOT NULL,
    sku VARCHAR(20),
    qty INT,
    CONSTRAINT fk_export_order_line_order FOREIGN KEY (order_id) REFERENCES export_order (id)
) ENGINE = InnoDB;

CREATE TABLE export_line_tag (
    id INT PRIMARY KEY,
    line_id INT NOT NULL,
    tag VARCHAR(20),
    CONSTRAINT fk_export_line_tag_line FOREIGN KEY (line_id) REFERENCES export_order_line (id)
) ENGINE = InnoDB;

INSERT INTO export_customer (id, name, vip) VALUES
    (1, 'Acme', TRUE),
    (2, 'Globex', NULL);

INSERT INTO export_order (id, customer_id, placed_on, total, note) VALUES
    (100, 1, '2024-01-05', 99.90, 'rush'),
    (101, 1, '2024-02-10', NULL, NULL),
    (102, NULL, '2024-03-01', 5.00, NULL);

INSERT INTO export_order_line (id, order_id, sku, qty) VALUES
    (1000, 100, 'A-1', 2),
    (1001, 100, 'B-2', NULL);

INSERT INTO export_line_tag (id, line_id, tag) VALUES
    (1, 1000, 'fragile'),
    (2, 1000, 'heavy');
