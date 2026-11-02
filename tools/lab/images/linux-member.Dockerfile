# Lab Linux pool member (L30). Context = the dotnet publish folder of the probe (lab_build project `probe`).
# Build: docker build --build-arg PROJECT_DLL=meridian-lab-probe.dll --label meridian-lab=1 -t meridian-lab/linux-member:<build_id> <publish dir>
# The member runs no service. It sleeps, so `docker exec` can run the probe's footprint mode against it.
FROM mcr.microsoft.com/dotnet/runtime:10.0
COPY . /opt/lab/probe/
WORKDIR /opt/lab/probe
CMD ["sleep", "infinity"]
