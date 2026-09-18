using Bank.DAL.Models.BaseModels;
using Bank.Repo.Implementations;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Repo.Interfaces
{
    public interface IUnitOfWork
    {
        Task<int> CompleteAsync();
        IGenericRepo<T> Repository<T> () where T : BaseEntity;
    }
}

