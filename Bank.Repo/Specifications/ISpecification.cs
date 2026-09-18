using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Bank.Repo.Specifications
{
    public interface ISpecification<T>
    {
        Expression<Func<T, bool>>? Criteria { get; }
        IReadOnlyList<Expression<Func<T,object>>> Includes { get; }
        IReadOnlyList<Func<IQueryable<T>,IIncludableQueryable<T,object>>> AllIncludes { get; }
        Expression<Func<T,object>>? OrderBy { get; }
        Expression<Func<T,object>>? OrderByDescending { get; }
        int Skip { get; }
        int Take { get; }
        bool IsPaginable { get; }
        bool AsNoTracking { get; }
        bool IsSplitQuery { get; }
    }
}

