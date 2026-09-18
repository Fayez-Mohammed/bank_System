using Bank.DAL.Context;
using Bank.DAL.Models.BaseModels;
using Bank.Repo.Interfaces;
using Bank.Repo.Specifications;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Bank.Repo.Implementations
{
    public class GenericRepo<TEntity> : IGenericRepo<TEntity> where TEntity : BaseEntity
    {
       readonly AppDbContext _context;
       readonly DbSet<TEntity> dbset;
        public GenericRepo(AppDbContext context)
        {
            _context=context;
            dbset= context.Set<TEntity>();
        }
        public async Task<TEntity> AddAsync(TEntity entity)
        {
            await dbset.AddAsync(entity);
            return entity;
        }

        public async Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities)
        {
            await dbset.AddRangeAsync(entities);
            return entities;
        }

        public async Task<IReadOnlyList<TEntity>> GetAllAsync(bool asNoTracking=false)
        {
          IQueryable<TEntity> query = dbset;
            if(asNoTracking)
                query=query.AsNoTracking();
            return await query.ToListAsync();
            
        }

        public async Task<TEntity> GetByIdAsync(Guid Id, bool asNoTracking=false)
        {
            IQueryable<TEntity>  query = dbset;
            if(asNoTracking)
                query = query.AsNoTracking();
            
           return await query.FirstOrDefaultAsync(x=>x.Id==Id);

        }
    

        public  Task RemoveAsync(TEntity item)
        {
            dbset.Remove(item);
            return Task.CompletedTask;
        }

        public Task RemoveRangeAsync(IEnumerable<TEntity> entities)
        {
            dbset.RemoveRange(entities);
            return Task.CompletedTask;
        }

        public  Task UpdateAsync(TEntity entity)
        {
            dbset.Update(entity);
           
            return Task.CompletedTask;

        }
        public  Task UpdateRangeAsync(List<TEntity>entities)
        {
            dbset.UpdateRange(entities);
           
            return Task.CompletedTask;

        }
        
        //Specifications
        private IQueryable<TEntity> ApplySpecification(ISpecification<TEntity> spec)
        {
          
            return SpecificationEvaluator<TEntity>.GetQuery(dbset.AsQueryable(), spec);
        }

        public async Task<IReadOnlyList<TEntity>> SelectAsync(ISpecification<TEntity> spec)
        {
            return await ApplySpecification(spec).ToListAsync();
          
        }
        public async Task<bool> AnyAsync(ISpecification<TEntity> spec)
        {
            return await ApplySpecification(spec).AnyAsync();
          
        }
   

       public async Task<TEntity?> GetEntityWithSpecificationAsync(ISpecification<TEntity> spec)
        {
            return await ApplySpecification(spec).FirstOrDefaultAsync();
        }

       public async Task<int> CountAsync(ISpecification<TEntity> spec)
        {
            return await ApplySpecification(spec).CountAsync();
        }

        public async Task<IReadOnlyList<TResult>> SelectAsync<TResult>(ISpecification<TEntity> spec
            ,Expression<Func<TEntity,TResult>> selector)
        {
            return await ApplySpecification(spec).Select(selector).ToListAsync();

        }

       public async Task<TResult?> GetEntityWithSpecificationAsync<TResult>(ISpecification<TEntity> spec,
           Expression<Func<TEntity,TResult>> selector) 
        {
            return await ApplySpecification(spec).Select(selector).FirstOrDefaultAsync();

        }

        public async Task<TResult?> MaxAsync<TResult>(ISpecification<TEntity> spec, Expression<Func<TEntity, TResult>> selector)
        {
            return await ApplySpecification(spec).DefaultIfEmpty().MaxAsync(selector);
        }

        public async Task<TResult?> MinAsync<TResult>(ISpecification<TEntity> spec, Expression<Func<TEntity, TResult>> selector)
        {
            return await ApplySpecification(spec).DefaultIfEmpty().MinAsync(selector);
        }
        public async Task<bool>AnyAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await dbset.AnyAsync(predicate);
        }
    }
}

