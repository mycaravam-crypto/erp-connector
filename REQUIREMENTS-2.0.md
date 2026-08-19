# ERP Connector 2.0 – Requirements

Build a generic ERP connector that transfers configurable datasets from **Master System A** to **Slave System B**. The two systems have completely different database schemas and no native/direct integration.

The connector must not be limited to predefined objects or structures. A user must be able to define **arbitrary hierarchical/nested exports** using data and relationships available in System A.

## Core Example

A single export could contain:

* Articles

  * related Orders

    * Serial Numbers
    * Shipment Details
  * related Manufacturers

    * multiple Addresses

The structure and nesting depth must be dynamically configurable rather than hardcoded.

## Primary UI Requirement

The main feature is a highly flexible but easy-to-use **visual export configuration UI**.

The UI should provide a tree-based export builder where users can:

1. Select a root entity/table/object from System A.
2. Add fields to the export.
3. Add related entities as child nodes.
4. Continue adding relationships recursively to create arbitrary nested structures.
5. Remove, reorder, rename, and configure nodes and fields.
6. See the complete resulting export structure as a visual tree.
7. Preview representative output data before saving or executing the export.

The UI should hide unnecessary database complexity while still exposing advanced configuration when required.

## Field Mapping

Every exported field must support explicit mapping between both systems.

For each field, the user must be able to configure at minimum:

`Source field in System A → Target field/path in System B`

Example:

`article.article_number → product.sku`

Mappings must work independently at every level of the nested structure.

The configuration should also support:

* custom target field names
* field exclusion
* constants/default values
* optional value transformation
* null/default handling
* data-type conversion

## Export Configuration

An export definition must be a persistent configuration containing:

* export name and description
* root entity
* selected fields
* nested relationships
* filters/conditions
* field mappings
* transformations
* output structure
* target configuration
* execution schedule

Users must be able to **save, edit, duplicate, test, enable/disable, and manually execute** export configurations.

## Scheduling

Saved exports must support scheduled execution, for example:

* manual
* hourly
* daily
* weekly
* configurable cron-style schedule

Execution history and status should be visible to the user.

## Architectural Principle

The connector should separate:

**Source Model → Export Model → Mapping/Transformation → Target Model**

The export definition must therefore be metadata/configuration-driven rather than implemented separately for every ERP object.

The backend must be able to interpret the saved tree definition recursively and generate the corresponding hierarchical dataset at runtime.

## Key Quality Requirements

* **Usability:** complex exports must be configurable without programming.
* **Maintainability:** adding new entities or fields should not require implementing a new export type.
* **Flexibility:** arbitrary nesting and field mappings must be supported.
* **Reliability:** failed exports must be logged and diagnosable without producing silently incomplete data.
* **Security:** access to source data, mappings, credentials, and export execution must be permission-controlled.
* **Traceability:** every execution should record configuration/version, timestamp, result, and errors.
