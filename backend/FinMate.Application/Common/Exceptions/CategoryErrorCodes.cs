namespace FinMate.Application.Common.Exceptions;

public static class CategoryErrorCodes
{
    public const string SlugDuplicate = "CATEGORY_SLUG_DUPLICATE";
    public const string HasTransactions = "CATEGORY_HAS_TRANSACTIONS";
    public const string CannotHide = "CATEGORY_CANNOT_HIDE";
    public const string ParentInvalid = "CATEGORY_PARENT_INVALID";
}
