---
title: "Unified Service Scheduler - Database Documentation"
tags: [database, service-scheduler, index]
---

# Database documentation

SQL Server stores dealership configuration, customers, vehicles, scheduling resources, and appointments. EF Core migrations define the schema; a stored procedure protects atomic booking. Redis provides an optional service-catalog cache. See [[architecture]] for the application design.

| Document | Purpose |
| --- | --- |
| [[database/overview\|Overview]] | Relationships and booking integrity rules. |
| [[database/schema\|Schema]] | Tables, columns, keys, constraints, indexes, and booking procedure. |
| [[database/migration\|Migrations]] | Initialization, migration files, compatibility, and recovery. |
| [[database/cache\|Cache]] | Catalog cache contract, expiry, fallback, and invalidation. |
| [[database/maintenance\|Maintenance guideline]] | Documentation and verification requirements for data changes. |

Update the affected references alongside changes to tables, columns, relationships, constraints, indexes, procedures, seeds, or cache contracts. Keep historical change details in dedicated development records.
