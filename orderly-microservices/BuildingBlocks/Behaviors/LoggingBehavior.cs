using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BuildingBlocks.Behaviors;

public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger) 
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>
    where TResponse : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("[START] Handling request: {Request}, Response: {Response}, Request data: {RequestData}", typeof(TRequest).Name, typeof(TResponse).Name, request);
        }

        var timer = new Stopwatch();
        timer.Start();

        var response = await next();

        timer.Stop();
        var timerTaken = timer.Elapsed;
        if(timerTaken.TotalSeconds > 3)
        {

            logger.LogWarning("[PERFORMANCE] Handling request: {Request}, Response: {Response}, Request data: {RequestData}, Elapsed time: {ElapsedTime}", typeof(TRequest).Name, typeof(TResponse).Name, request, timerTaken);
        }
        else
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("[END] Handling request: {Request}, Response: {Response}, Request data: {RequestData}, Elapsed time: {ElapsedTime}", typeof(TRequest).Name, typeof(TResponse).Name, request, timerTaken);
            }
        }

        return response;
    }
}
