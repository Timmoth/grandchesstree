-- Enable TimescaleDB
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- Create a table for perft time-series data
CREATE TABLE IF NOT EXISTS perft_readings (
    time TIMESTAMPTZ NOT NULL,
    account_id BIGINT NOT NULL,
    worker_id SMALLINT NOT NULL,
    nodes BIGINT NOT NULL,
    occurrences SMALLINT NOT NULL,
    duration BIGINT NOT NULL,
    depth SMALLINT NOT NULL,
    root_position_id SMALLINT NOT NULL,
    task_type SMALLINT NOT NULL
);

-- Convert the table into a hypertable
SELECT create_hypertable(
    'perft_readings', 
    'time',                               -- Partition by the time column
    chunk_time_interval => INTERVAL '1 hour',  -- Create chunks based on a daily interval
    if_not_exists => TRUE
);

-- Create indexes
CREATE INDEX idx_account_id ON perft_readings (account_id);
CREATE INDEX idx_root_position_depth ON perft_readings (root_position_id, depth);
CREATE INDEX idx_task_type ON perft_readings (task_type);
