using System.Reflection;
using ExamService.Application.Common;
using ExamService.Application.Features;
using ExamService.Application.Features.Exams;
using ExamService.Application.Features.Identity;
using ExamService.Application.Features.Reports;
using ExamService.Application.Features.Sessions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ExamService.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<IExamService, Features.ExamService>();
        services.AddScoped<IAttemptService, Features.AttemptService>();
        services.AddScoped<IReportService, Features.ReportService>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IEvidenceService, EvidenceService>();
        return services;
    }
}
