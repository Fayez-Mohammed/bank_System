using Bank.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bank.API.Filters;

public class ResponseWrapperFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult objectResult)
        {
            if (objectResult.Value is IApiResponse apiResponse)
            {
                apiResponse.TraceId ??= context.HttpContext.TraceIdentifier;
                if (objectResult.StatusCode.HasValue)
                    apiResponse.StatusCode = objectResult.StatusCode.Value;
            }
            else
            {
                var statusCode = objectResult.StatusCode ?? context.HttpContext.Response.StatusCode;
                var response = new ApiResponse<object>
                {
                    Success = statusCode is >= 200 and < 300,
                    Data = objectResult.Value,
                    Message = statusCode is >= 200 and < 300 ? "Success" : "Failed",
                    TraceId = context.HttpContext.TraceIdentifier,
                    StatusCode = statusCode
                };

                context.Result = new ObjectResult(response)
                {
                    StatusCode = statusCode
                };
            }
        }
        else if (context.Result is StatusCodeResult statusCodeResult)
        {
            var isSuccess = statusCodeResult.StatusCode is >= 200 and < 300;
            var response = new ApiResponse<object>
            {
                Success = isSuccess,
                Data = null,
                Message = isSuccess ? "Success" : "Failed",
                TraceId = context.HttpContext.TraceIdentifier,
                StatusCode = statusCodeResult.StatusCode
            };

            context.Result = new ObjectResult(response)
            {
                StatusCode = statusCodeResult.StatusCode
            };
        }

        await next();
    }
}
//using Bank.Shared.DTOs;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.ApiExplorer;
//using Microsoft.AspNetCore.Mvc.Filters;
//using Microsoft.EntityFrameworkCore.Storage;

//namespace Bank.API.Filters
//{
//    public class ResponseWrapperFilter : IAsyncResultFilter
//    {
//        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
//        {
//            if(context.Result is ObjectResult objectResult )
//            {
//                if(objectResult.Value is ProblemDetails)
//                {
//                    await next();
//                    return;
//                }
//                bool isAlreadyWrapped = objectResult.Value is IApiResponse;
//               // bool isProblemDetails = objectResult.Value is ProblemDetails;
//                if (!isAlreadyWrapped /*&& !isProblemDetails*/)
//                {
//                    var response = new ApiResponse<object>
//                    {
//                        Data = objectResult.Value,
//                        Message = "Success",
//                        TraceId = context.HttpContext.TraceIdentifier,
//                        StatusCode = objectResult.StatusCode ?? context.HttpContext.Response.StatusCode
//                    };
//                    context.Result = new ObjectResult(response)
//                    {
//                        StatusCode = objectResult.StatusCode ?? context.HttpContext.Response.StatusCode,
//                    };
//                }
//            }
//            else if(context.Result is StatusCodeResult statusCodeResult)
//            {
//                var response = new ApiResponse<object>
//                {
//                    Data = null,
//                    Message = "Success",
//                    TraceId = context.HttpContext.TraceIdentifier,
//                    StatusCode = statusCodeResult.StatusCode 
//                };
//                context.Result = new ObjectResult(response)
//                {
//                    StatusCode = statusCodeResult.StatusCode
//                };

//            }
//            await next();
//        }
//    }
//}

