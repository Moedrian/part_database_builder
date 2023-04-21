# Part Library Builder

## README

The PART LIBRARY BUILDER is a tool that can create a customized part database for customers. Users can import their bill of materials (BOM) from a .csv file into this tool, and part information will then be inserted into the database. Users can also directly edit the part information in the database.  As users import more and more part data over time, they can eventually generate a full BOM by simply providing a .csv file containing only the drawing reference and part number columns.

## 0. Why use SQL in this project?

* Data integrity - Databases enforce data types, constraints, etc. to ensure data quality. Text files have no data integrity checks.
* Query capabilities - Databases provide rich querying capabilities, allowing you to slice and filter data in complex ways. Text files require manual parsing and filtering.
* Data manipulation - It is much easier to insert, update, and delete data in a database. Again, text files require manual manipulation.
* Reliability - Databases provide reliability features like transactions, backups, and replication. Text files could be easily corrupted or lost.

## 1. Runtime

.NET 6.0 Windows x64

## 2. Components

* A WPF project presenting data for user.
* SQLite aka Microsoft.Data.Sqlite acting as an data engine.
* Database file storing the part library.
