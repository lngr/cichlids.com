CREATE TABLE tanks (id integer PRIMARY KEY, name text NOT NULL, litres integer NOT NULL);
CREATE TABLE observations (id integer PRIMARY KEY, tank_id integer REFERENCES tanks, note text NOT NULL);
CREATE TABLE measurements (id integer PRIMARY KEY, tank_id integer REFERENCES tanks, value numeric NOT NULL);
INSERT INTO tanks SELECT i, 'Synthetic tank ' || i, 100 + i % 500 FROM generate_series(1, 2000) i;
INSERT INTO observations SELECT i, 1 + i % 2000, md5(i::text) FROM generate_series(1, 6000) i;
INSERT INTO measurements SELECT i, 1 + i % 2000, i / 100.0 FROM generate_series(1, 8000) i;
