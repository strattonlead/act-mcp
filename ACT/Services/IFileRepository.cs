using System;
using System.Threading.Tasks;
using ACT.Models;

namespace ACT.Services;

public interface IFileRepository
{
    Task<UploadedFile?> GetByIdAsync(Guid id);
    Task CreateAsync(UploadedFile file);
    Task DeleteAsync(Guid id);
}
