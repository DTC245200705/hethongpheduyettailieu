SELECT 'CREATE DATABASE document_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'document_db')\gexec

SELECT 'CREATE DATABASE notification_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'notification_db')\gexec
