os.putenv ('DOCKER_BUILDKIT' , '1' )
isWindows = True if os.name == "nt" else False

name = 'macsux/dotnet-accelerator'
expected_ref = "%EXPECTED_REF%" if isWindows else "$EXPECTED_REF"
rid = "ubuntu.18.04-x64"
configuration = "Debug"
isWindows = True if os.name == "nt" else False

local_resource(
  'live-update-build',
  cmd= 'dotnet publish src/MyProjectGroup.DotnetAccelerator --configuration ' + configuration + ' --runtime ' + rid + ' --self-contained false --output ./src/MyProjectGroup.DotnetAccelerator/bin/.buildsync',
  deps=['./src/MyProjectGroup.DotnetAccelerator/bin/' + configuration],
  ignore=['./src/MyProjectGroup.DotnetAccelerator/bin/**/' + rid]
)

custom_build(
        name,
        'docker build . -f ./src/MyProjectGroup.DotnetAccelerator/Dockerfile -t ' + expected_ref,
        deps=["./src/MyProjectGroup.DotnetAccelerator/bin/.buildsync", "./src/MyProjectGroup.DotnetAccelerator/Dockerfile", "./config"],
        live_update=[
            sync('./src/MyProjectGroup.DotnetAccelerator/bin/.buildsync', '/app'),
            sync('./config', '/app/config'),
        ]
    )





k8s_yaml(['kubernetes/deployment.yaml'])
k8s_resource('dotnet-accelerator', port_forwards=[8080,22])