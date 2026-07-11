namespace Catalog.API.Exceptions;

public class BulkOrderUploadNotFoundException(int id) : NotFoundException("BulkOrderUpload", id)
{
}