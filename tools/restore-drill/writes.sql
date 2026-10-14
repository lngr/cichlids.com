BEGIN;
INSERT INTO tanks SELECT i, 'Synthetic WAL tank ' || i, 200 FROM generate_series(2001, 2500) i;
INSERT INTO observations SELECT i, 2001 + i % 500, md5('wal' || i) FROM generate_series(6001, 7000) i;
INSERT INTO measurements SELECT i, 2001 + i % 500, i / 200.0 FROM generate_series(8001, 10000) i;
UPDATE tanks SET litres = litres + 1 WHERE id % 7 = 0;
UPDATE observations SET note = 'WAL update ' || id WHERE id % 11 = 0;
DELETE FROM measurements WHERE id % 13 = 0;
COMMIT;
