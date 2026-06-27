namespace Catalog.API.Exceptions;

public class CustomerFeedbackNotFoundException(int id) : NotFoundException("CustomerFeedback", id)
{
}