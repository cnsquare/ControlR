using ControlR.Libraries.Branding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ControlR.Web.Server.OpenApi;

public class V1ApiDocumentInfoTransformer : IOpenApiDocumentTransformer
{
  public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
  {
    document.Info.Title = $"{BrandingConstants.BrandName} API v1";
    document.Info.Description = "Versioned stable API";
    document.Info.Version = "v1";
    return Task.CompletedTask;
  }
}
