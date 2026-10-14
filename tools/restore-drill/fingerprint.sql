SELECT 'tanks', count(*), md5(string_agg(row_to_json(t)::text, E'\n' ORDER BY id)) FROM tanks t
UNION ALL
SELECT 'observations', count(*), md5(string_agg(row_to_json(t)::text, E'\n' ORDER BY id)) FROM observations t
UNION ALL
SELECT 'measurements', count(*), md5(string_agg(row_to_json(t)::text, E'\n' ORDER BY id)) FROM measurements t
ORDER BY 1;
