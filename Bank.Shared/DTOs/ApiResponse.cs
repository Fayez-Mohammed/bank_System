using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Shared.DTOs
{
    public interface IApiResponse
    {
        bool Success { get; }
        string? Message { get; }
        List<string>? Errors { get; }
        string? TraceId { get; set; }
        int StatusCode { get; set; }
    }

    public class ApiResponse<T> : IApiResponse
    {
        public bool Success { get; init; }
        public string? Message { get; init; }
        public T? Data { get; init; }
        public List<string>? Errors { get; init; }
        public string? TraceId { get; set; }
        public int StatusCode { get; set; } = 200;

        public static ApiResponse<T> SuccessResponse(T data, string? message = null, int statusCode = 200) =>
            new()
            {
                Success = true,
                Data = data,
                Message = message,
                StatusCode = statusCode
            };

        public static ApiResponse<T> Fail(string message, List<string>? errors = null, int statusCode = 400) =>
            new()
            {
                Success = false,
                Message = message,
                Errors = errors ?? new List<string> { message },
                StatusCode = statusCode
            };
    }

    public class ApiResponse : ApiResponse<object>
    {
        public static ApiResponse SuccessResponse(string? message = null, int statusCode = 200) =>
            new()
            {
                Success = true,
                Data = null,
                Message = message,
                StatusCode = statusCode
            };

        public static new ApiResponse Fail(string message, List<string>? errors = null, int statusCode = 400) =>
            new()
            {
                Success = false,
                Message = message,
                Errors = errors ?? new List<string> { message },
                StatusCode = statusCode
            };
    }
    //public interface IApiResponse { }
    //public class ApiResponse<T>:IApiResponse
    //{
    //    public int StatusCode { get; set; }
    //    public string Message { get; set; } = "Success";
    //    public string TraceId { get; set; } = string.Empty;
    //    public T? Data { get; set; }
    //}
}

