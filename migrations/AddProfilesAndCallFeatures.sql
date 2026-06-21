-- ============================================================
-- Migration: AddProfilesAndCallFeatures
-- Features 1-6 schema changes
-- Run once in Supabase SQL Editor
-- ============================================================

-- ── Feature 1: First-line / backup toggle ───────────────────
ALTER TABLE public.assistant_settings
    ADD COLUMN IF NOT EXISTS call_mode VARCHAR(20) NOT NULL DEFAULT 'first_line';

-- ── Feature 3: Bot active hours enabled flag ────────────────
ALTER TABLE public.assistant_settings
    ADD COLUMN IF NOT EXISTS bot_active_hours_enabled BOOLEAN NOT NULL DEFAULT FALSE;

-- ── Feature 4: Caller classification on call sessions ───────
ALTER TABLE public.call_sessions
    ADD COLUMN IF NOT EXISTS caller_classification VARCHAR(20) NULL;

-- ── Feature 5: Auto-transfer per appointment type ───────────
ALTER TABLE public.appointment_types
    ADD COLUMN IF NOT EXISTS auto_transfer_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS auto_transfer_department_id BIGINT NULL
        REFERENCES public.company_departments(department_id) ON DELETE SET NULL;

-- ── Feature 6: Assistant profiles ───────────────────────────
CREATE TABLE IF NOT EXISTS public.assistant_profiles (
    id                      SERIAL PRIMARY KEY,
    company_id              SMALLINT NOT NULL REFERENCES public.company(company_id) ON DELETE CASCADE,
    name                    VARCHAR(100) NOT NULL,
    is_active               BOOLEAN NOT NULL DEFAULT FALSE,
    system_prompt           TEXT,
    greeting_message        TEXT,
    language                VARCHAR(10) NOT NULL DEFAULT 'nl',
    call_mode               VARCHAR(20) NOT NULL DEFAULT 'first_line',
    after_hours_mode        VARCHAR(5),
    bot_active_hours_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- At most one active profile per company (partial unique index).
CREATE UNIQUE INDEX IF NOT EXISTS uix_assistant_profiles_active
    ON public.assistant_profiles(company_id)
    WHERE is_active = TRUE;

-- ── Feature 3: Bot active hours table ───────────────────────
CREATE TABLE IF NOT EXISTS public.bot_active_hours (
    id          SERIAL PRIMARY KEY,
    company_id  SMALLINT NOT NULL REFERENCES public.company(company_id) ON DELETE CASCADE,
    day_of_week SMALLINT NOT NULL,   -- 1=Mon … 7=Sun
    open_time   TIME NOT NULL,
    close_time  TIME NOT NULL,
    profile_id  INTEGER NULL REFERENCES public.assistant_profiles(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_bot_active_hours_company_day
    ON public.bot_active_hours(company_id, day_of_week);
