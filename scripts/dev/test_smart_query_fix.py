import unittest


class SmartQueryModelRoutingTests(unittest.TestCase):
    def test_custom_openai_compatible_default_provider_is_supported(self):
        from core.models import ModelRouter, OpenAIAdapter

        config = {
            "models": {
                "default": "local",
                "local": {
                    "base_url": "http://127.0.0.1:1234/v1",
                    "api_key": "not-needed",
                    "model": "qwen-local",
                },
                "ollama": {"base_url": "http://localhost:11434", "model": "qwen2.5:7b"},
                "openai": {"base_url": "https://api.openai.com/v1", "model": "gpt-4o-mini"},
            }
        }

        adapter = ModelRouter(config).get()

        self.assertIsInstance(adapter, OpenAIAdapter)
        self.assertEqual(adapter.model, "qwen-local")
        self.assertEqual(adapter.api_url, "http://127.0.0.1:1234/v1/chat/completions")


class SmartQuerySqlTests(unittest.TestCase):
    def test_mysql_table_names_are_quoted_after_from_and_join(self):
        from core.smart_query import _quote_mysql_table_names

        sql = "SELECT COUNT(*) FROM order JOIN user ON order.user_id = user.id"
        metas = [{"table_name": "order"}, {"table_name": "user"}]

        self.assertEqual(
            _quote_mysql_table_names(sql, metas),
            "SELECT COUNT(*) FROM `order` JOIN `user` ON `order`.user_id = `user`.id",
        )

    def test_generate_sql_fallback_quotes_mysql_reserved_table_name(self):
        from core.smart_query import SmartQueryService

        class BrokenModel:
            def chat(self, messages, **kwargs):
                raise RuntimeError("model unavailable")

        svc = SmartQueryService()
        svc.model = BrokenModel()

        sql = svc.generate_sql(
            "铅笔数量是多少",
            [{"table_name": "order", "columns": [], "primary_keys": [], "foreign_keys": []}],
            db_type="mysql",
        )

        self.assertEqual(sql, "SELECT * FROM `order` LIMIT 10")


if __name__ == "__main__":
    unittest.main()
