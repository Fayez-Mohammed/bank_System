using Bank.Repo.Specifications;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Text;
using Bank.DAL.Models.BaseModels;
using Microsoft.EntityFrameworkCore;
namespace Bank.Repo.Implementations
{
    public class SpecificationEvaluator<TEntity> where TEntity : BaseEntity
    {
        public static IQueryable<TEntity> GetQuery(
            IQueryable<TEntity> QueryInput,
            ISpecification<TEntity> spec
            )
        {
            var query = QueryInput.AsQueryable();
            if (spec.Criteria != null)
                query = query.Where(spec.Criteria);
            if (spec.AsNoTracking)
                query = query.AsNoTracking();

            if (spec.IsSplitQuery)
                query = query.AsSplitQuery();

            if (spec.Includes.Any())
                foreach ( var include in spec.Includes)
                query = query.Include(include);

            if (spec.AllIncludes.Any())
                foreach (var include in spec.AllIncludes)
                    query = include(query);

            if (spec.OrderBy != null)
                query= query.OrderBy(spec.OrderBy);
            else if(spec.OrderByDescending != null)
            {
                query= query.OrderByDescending(spec.OrderByDescending);
            }

            if (spec.IsPaginable)
            {
               query=query.Skip(spec.Skip).Take(spec.Take);
            }
           
            return query;
        }
    }
}

