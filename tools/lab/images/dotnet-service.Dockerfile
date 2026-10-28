# Lab service image (L09). Context = the dotnet publish folder for one project.
# Build: docker build --build-arg PROJECT_DLL=<name>.dll --label meridian-lab=1 -t meridian-lab/<project>:<build_id> <publish dir>
#
# ENTRYPOINT: the exec form cannot expand an ARG, so the DLL name is carried in ENV and the
# entrypoint is a single `sh -c` line that ends in `exec`, so dotnet is PID 1 and gets signals.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT_DLL
ENV PROJECT_DLL=${PROJECT_DLL}
WORKDIR /app
COPY . .
ENV ASPNETCORE_URLS=http://+:8080
USER app
ENTRYPOINT ["sh", "-c", "exec dotnet /app/$PROJECT_DLL"]
