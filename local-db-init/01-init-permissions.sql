-- init-scripts/01-init-permissions.sql

-- 1. Create the restricted user that your Blazor app will use day-to-day
CREATE ROLE kisatsingen_web_app WITH LOGIN PASSWORD 'kisatsingen_web_app_local_dev_pass';

-- 2. Grant basic entryway access to the main database lobby
GRANT CONNECT ON DATABASE kisatsingen_dev_db TO kisatsingen_web_app;

-- 3. Automate CRUD permissions on any FUTURE tables that EF Core creates.
-- This ensures that whenever 'local_user' creates tables via migrations, 
-- 'kisatsingen_web_app' instantly gets read/write rights without manual grants.
ALTER DEFAULT PRIVILEGES FOR ROLE local_user IN SCHEMA public -- In the future, whenever the specific user local_user creates a new table or sequence inside the public schema, automatically apply the following permissions to other users.
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kisatsingen_web_app;

-- Grants permission to use serial/identity primary keys (inserting new auto generate id's)
ALTER DEFAULT PRIVILEGES FOR ROLE local_user IN SCHEMA public -- In the future, whenever the specific user local_user creates a new table or sequence inside the public schema, automatically apply the following permissions to other users.
GRANT SELECT, USAGE ON SEQUENCES TO kisatsingen_web_app;

-- If you modify this file, you must stop the container and forcefully delete the 'kisatsingen_pgdata' volume (docker compose down -v)
-- Boot it back up to force a clean, first-time initialization (docker compose up -d)