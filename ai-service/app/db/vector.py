"""Kiểu cột ``vector(n)`` của pgvector cho SQLAlchemy (không thêm package ``pgvector``: ta chỉ cần khai báo cột; chèn và truy vấn dùng SQL thô với ``CAST(:v AS vector)``)."""

from sqlalchemy.types import UserDefinedType


class Vector(UserDefinedType):
    cache_ok = True

    def __init__(self, dim: int) -> None:
        self.dim = dim

    def get_col_spec(self, **kw) -> str:
        return f"vector({self.dim})"


def to_literal(values: list[float]) -> str:
    """[0.1, 0.2] → "[0.1,0.2]": dạng chữ pgvector đọc được, gắn vào tham số rồi ``CAST(:v AS vector)``."""
    return "[" + ",".join(repr(float(v)) for v in values) + "]"
