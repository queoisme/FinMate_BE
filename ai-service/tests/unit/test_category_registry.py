"""Tập slug danh mục hợp lệ lúc chạy: mốc trong code khi chưa có DB, đọc ``kb_categories`` khi có (không cần Postgres: dùng phiên giả)."""

import pytest

from app.services import category_registry


class FakeResult:
    def __init__(self, rows):
        self._rows = rows

    def all(self):
        return self._rows


class FakeSession:
    def __init__(self, rows):
        self._rows = rows

    async def execute(self, _statement):
        return FakeResult(self._rows)


@pytest.fixture(autouse=True)
def clean_registry():
    category_registry.reset()
    yield
    category_registry.reset()


def test_without_a_database_the_code_baseline_applies():
    assert category_registry.is_known("food") and category_registry.is_known("income")
    assert category_registry.is_spend("food") and not category_registry.is_spend("income")  # thu nhập không phải khoản chi
    assert not category_registry.is_known("pets") and not category_registry.is_known(None)


async def test_refresh_reads_active_backend_categories():
    await category_registry.refresh(FakeSession([("food", "expense"), ("pets", "expense"), ("bonus", "income")]))

    assert category_registry.is_known("pets") and category_registry.is_known("bonus")
    assert category_registry.is_spend("pets") and not category_registry.is_spend("bonus")
    assert not category_registry.is_known("transport")  # backend đã tắt hoặc chưa có: không còn được nhận cho dữ liệu mới


async def test_an_empty_table_keeps_the_baseline_instead_of_rejecting_everything():
    await category_registry.refresh(FakeSession([]))

    assert category_registry.is_known("food")


async def test_spend_list_keeps_code_order_then_new_categories_alphabetically():
    await category_registry.refresh(FakeSession([("zoo", "expense"), ("food", "expense"), ("art", "expense"), ("transport", "expense")]))

    assert category_registry.spend_list() == ["food", "transport", "art", "zoo"]


async def test_reset_returns_to_the_baseline():
    await category_registry.refresh(FakeSession([("pets", "expense")]))
    category_registry.reset()

    assert category_registry.is_known("food") and not category_registry.is_known("pets")
