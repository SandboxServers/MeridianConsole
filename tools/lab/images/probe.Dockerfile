# Lab probe image (L36): the webhook and email sink runs as `meridian-lab-sink` from this image.
# Context = the publish folder of Dhadgar.Lab.Probe (AssemblyName meridian-lab-probe).
# Build: docker build --label meridian-lab=1 -t meridian-lab/probe:<build_id> <publish dir>
# Run as the sink: docker run ... meridian-lab/probe:<build_id> sink
#
# The base is the ASP.NET Core runtime, not the bare runtime image: the sink is a minimal API and needs the
# Microsoft.AspNetCore.App shared framework (the probe project references it).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY . .
ENV ASPNETCORE_URLS=http://+:8080
USER app
ENTRYPOINT ["dotnet", "/app/meridian-lab-probe.dll"]
CMD ["sink"]
