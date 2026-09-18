using Bank.DAL.Models.BaseModels;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Bank.Repo.Specifications
{
    public class BaseSpecification<T> : ISpecification<T> where T : BaseEntity
    {
        private readonly List<Expression<Func<T, object>>> _include = new();
        private readonly List<Func<IQueryable<T>,IIncludableQueryable<T,object>>> _allIncude= new();
        public Expression<Func<T, bool>>? Criteria { get; private set; }

        public IReadOnlyList<Expression<Func<T, object>>> Includes  => _include;

        public IReadOnlyList<Func<IQueryable<T>, IIncludableQueryable<T, object>>> AllIncludes => _allIncude;

       public Expression<Func<T, object>>? OrderBy { get; private set; }

       public Expression<Func<T, object>>? OrderByDescending { get;private set; }

       public int Skip { get;private set;  }

       public int Take {  get;private set; }

       public bool IsPaginable {  get;private set; }
        public bool AsNoTracking { get;private set; }
        public bool IsSplitQuery { get;private set; }

       

        public BaseSpecification()
        {
            
        }
        public BaseSpecification(Expression<Func<T,bool>> CriteriaExpression)
        {
            Criteria = CriteriaExpression;
        }
        public void AddInclude(Expression<Func<T,object>> IncludeExpression)=>_include.Add(IncludeExpression);
        public void AddInclude(Func<IQueryable<T>,IIncludableQueryable<T,object>>AllIncludeExpression)=>_allIncude.Add(AllIncludeExpression);
        public void AddOrderBy(Expression<Func<T,object>>OrderByExpression)=>OrderBy = OrderByExpression;
        
        public void AddOrderByDesc(Expression<Func<T,object>>OrderByDescExpression)=> OrderByDescending=OrderByDescExpression;
        
        public void ApplyPagination(int skip,int take)
        {
            Skip = skip;
            Take = take;
            IsPaginable = true;
        }
        public void ApplyNoTracking()=> AsNoTracking = true;
        public void ApplySplitQuery()=> IsSplitQuery = true;
    }
}

