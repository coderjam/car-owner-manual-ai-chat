INSERT INTO tb_user (username, password_hash)
VALUES ('demo', 'dev-password-hash')
ON CONFLICT (username) DO NOTHING;

INSERT INTO tb_vehicle (id, brand, model, year, engine, configuration)
VALUES (1, '丰田', '凯美瑞', 2026, '智能电混双擎', '通用版')
ON CONFLICT (id)
DO UPDATE SET
    brand = EXCLUDED.brand,
    model = EXCLUDED.model,
    year = EXCLUDED.year,
    engine = EXCLUDED.engine,
    configuration = EXCLUDED.configuration;

SELECT setval('tb_user_id_seq', GREATEST((SELECT MAX(id) FROM tb_user), 1));
SELECT setval('tb_vehicle_id_seq', GREATEST((SELECT MAX(id) FROM tb_vehicle), 1));
