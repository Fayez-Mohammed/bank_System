namespace Bank.DAL.Models.BaseModels;

/// <summary>
/// Base class for every aggregate whose SQL Server primary key is a
/// uniqueidentifier (GUID) column, generated as NEWSEQUENTIALID() in the DB.
/// The Id is exposed as a string in the domain layer so that services,
/// DTOs and API contracts never need to know it is really a GUID.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
}
