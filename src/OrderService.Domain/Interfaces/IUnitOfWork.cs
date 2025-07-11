using System.Threading.Tasks;

namespace OrderService.Domain.Interfaces;

public interface IUnitOfWork
{
    Task<bool> CommitAsync();
}
