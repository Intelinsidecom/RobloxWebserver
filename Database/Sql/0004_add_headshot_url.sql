-- Adds a separate headshot URL column for user thumbnails
-- Safe to run multiple times: add column only if it does not exist
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'users'
          AND column_name  = 'headshot_url'
    ) THEN
        ALTER TABLE public.users
            ADD COLUMN headshot_url text;
    END IF;
END $$;
