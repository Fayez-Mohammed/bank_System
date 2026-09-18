
using Bank.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bank.API.Filters;

public class ErrorFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executedContext = await next();

        if (executedContext.Result is ObjectResult objectResult && objectResult.Value is IApiResponse apiResponse)
        {
            apiResponse.TraceId = context.HttpContext.TraceIdentifier;

            if (!apiResponse.Success)
            {
                objectResult.StatusCode = apiResponse.StatusCode != 200 ? apiResponse.StatusCode : 400;
            }
        }
    }
}
//using Bank.Shared.DTOs;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.Filters;

//namespace Bank.API.Filters
//{
//    public class ErrorFilter : IAsyncActionFilter
//    {
//        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
//        {
//            var executedContext = await next();
//            if(executedContext.Result is ObjectResult objectResult)
//            {
//                if (objectResult.Value is Result result)
//                {
//                    if (!result.IsSuccess)
//                    {
//                        objectResult.StatusCode = result.StatusCode;
//                        objectResult.Value = new ProblemDetails
//                        {
//                            Status = result.StatusCode,
//                            Detail = result.Error,
//                            Instance = context.HttpContext.Request.Path
//                        };
//                    }
//                    else
//                    {
//                        objectResult.Value = result.GetType().GetProperty("Value")?.GetValue(result);

//                    }

//                }
//            }
//        }
//    }
//}

