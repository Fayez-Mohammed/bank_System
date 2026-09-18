using Bank.DAL.Models.BaseModels;
using Bank.Repo.Specifications;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Bank.Repo.Interfaces
{
    public interface IGenericRepo<T> where T : BaseEntity
    {
        Task<T> AddAsync(T entity);
        Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities);
        Task UpdateAsync(T entity);
        Task UpdateRangeAsync(List<T> entities);
        Task RemoveAsync(T entity);
        Task RemoveRangeAsync(IEnumerable<T> entities);
        Task<T> GetByIdAsync(Guid Id,bool asNoTracking=false);
        Task<IReadOnlyList<T>> GetAllAsync(bool asNoTracking=false);
       // IQueryable<T> ApplySpecification(ISpecification<T> spec);
        Task<IReadOnlyList<T>> SelectAsync(ISpecification<T> spec);
        Task<bool> AnyAsync(ISpecification<T> spec);
        Task<IReadOnlyList<TResult>> SelectAsync<TResult>(ISpecification<T> spec, Expression<Func<T, TResult>> selector);
        Task<T?> GetEntityWithSpecificationAsync(ISpecification<T> spec);
        Task<TResult?> GetEntityWithSpecificationAsync<TResult>(ISpecification<T> spec, Expression<Func<T, TResult>> selector);
        Task<int> CountAsync(ISpecification<T> spec);
        Task<TResult?> MaxAsync<TResult>(ISpecification<T> spec,Expression<Func<T,TResult>> selector);
        Task<TResult?> MinAsync<TResult>(ISpecification<T> spec,Expression<Func<T,TResult>> selector);
        Task<bool> AnyAsync(Expression<Func<T,bool>> predicate);

    }
}

