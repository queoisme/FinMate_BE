using System.Linq.Expressions;

namespace FinMate.Application.Common;

/// <summary>
/// Ghép hai điều kiện dạng <see cref="Expression{TDelegate}"/> thành một mà EF vẫn dịch được sang SQL và <c>Compile()</c> vẫn chạy được trong bộ
/// nhớ. Dùng khi một điều kiện dùng chung (ví dụ <see cref="Transactions.LoanExclusion.NotLoan"/>) phải nằm BÊN TRONG một biểu thức khác.
/// </summary>
public static class Predicates
{
    public static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ReplaceParameter(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, rightBody), parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
