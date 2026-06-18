using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.RoslynExecution
{
    public sealed class UnityExecuteCSharpTool : IUnityMcpTool
    {
        private readonly RoslynExecutionAvailability _availability;

        internal UnityExecuteCSharpTool(RoslynExecutionAvailability availability)
        {
            _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        }

        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.execute_csharp",
            Title = "Unity Execute CSharp",
            Description = "Run trusted local Unity Editor automation from a __Run() method body. Edit Mode only. Submitted code runs inside the Unity Editor process, can mutate project state, and MVP does not guarantee interruption of dead loops or blocking calls.",
            DefaultTimeoutMs = 2000,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.Edit,
            SideEffect = UnityMcpToolSideEffect.RunsUserCode,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = RoslynExecutionContracts.ArgsSchemaJson
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();

            if (!RoslynExecutionJson.TryDeserializeArgs(context != null ? context.RawArgsJson : string.Empty, out ExecuteCSharpArgs args, out var failure))
            {
                return failure;
            }

            if (!RoslynExecutionValidation.TryValidate(args, out var validationMessage))
            {
                return RoslynExecutionResultFactory.ValidationFailed(
                    context,
                    RoslynExecutionContracts.PhaseValidationFailed,
                    validationMessage,
                    _availability.ProjectRoot);
            }

            try
            {
                var service = new RoslynExecutionService(_availability);
                return service.Execute(context, args, cancellation);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return new UnityMcpToolResult
                {
                    Success = false,
                    Status = RoslynExecutionContracts.PhaseExecutionFailed,
                    Summary = exception.Message,
                    Errors = new List<UnityMcpToolError>
                    {
                        new UnityMcpToolError
                        {
                            Code = "ROSLYN_EXECUTION_EXCEPTION",
                            Message = exception.ToString()
                        }
                    },
                    MetricsObjectJson = JsonConvert.SerializeObject(new RoslynExecutionMetrics
                    {
                        contractVersion = RoslynExecutionContracts.MetricsContractVersion,
                        success = false,
                        phase = RoslynExecutionContracts.PhaseExecutionFailed,
                        invocationId = context != null && !string.IsNullOrWhiteSpace(context.CommandId) ? context.CommandId : "execute_csharp_exception",
                        sourceHash = string.Empty,
                        stages = RoslynExecutionMetricsStages.CreateEmpty(),
                        result = RoslynExecutionResultEnvelope.CreateNull(),
                        error = exception.Message,
                        reportPath = null,
                        limit = RoslynExecutionContracts.CreateLimit()
                    }, Formatting.None)
                };
            }
        }
    }
}
