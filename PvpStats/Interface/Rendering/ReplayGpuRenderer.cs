using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using PvpStats.Analysis;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace PvpStats.Interface.Rendering;

// All D3D work happens on UiBuilder.Draw. A deferred command list preserves the host's entire immediate-context state.
internal unsafe sealed class ReplayGpuRenderer : IDisposable {
    private ID3D11Device* _device;
    private ID3D11DeviceContext* _deferred;
    private ID3D11DeviceContext* _immediate;
    private ID3D11Buffer* _mapVertices;
    private ID3D11Buffer* _mapIndices;
    private ID3D11Buffer* _dynamic;
    private ID3D11Buffer* _camera;
    private ID3D11VertexShader* _vs;
    private ID3D11PixelShader* _ps;
    private ID3D11InputLayout* _layout;
    private ID3D11RasterizerState* _raster;
    private ID3D11DepthStencilState* _depthState;
    private ID3D11DepthStencilState* _overlayDepth;
    private ID3D11BlendState* _overlayBlend;
    private ID3D11Texture2D* _color;
    private ID3D11Texture2D* _depth;
    private ID3D11Texture2D* _multisampleColor;
    private ID3D11RenderTargetView* _target;
    private ID3D11ShaderResourceView* _view;
    private ID3D11DepthStencilView* _depthView;
    private readonly uint _indexCount,_opaqueIndexCount;
    private const int MaxDynamicVertices=120_000;
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal uint SampleCount { get; private set; } = 1;
    internal ImTextureID Texture => new((ulong)_view);
    private bool _disposed;
    private bool _disposeScheduled;
    internal void DisposeAfterFrame(IUiBuilder builder) {
        if(_disposed||_disposeScheduled)return;
        _disposeScheduled=true;
        // ImGui may still hold this frame's SRV pointer. Release on the following UI frame.
        Action? cleanup=null;
        cleanup=()=> { builder.Draw-=cleanup;Dispose(); };
        builder.Draw+=cleanup;
    }

    [DllImport("d3dcompiler_47.dll",ExactSpelling=true)]
    private static extern int D3DCompile(byte* source,nuint length,byte* name,void* defines,void* include,byte* entry,byte* target,uint flags,uint flags2,ID3DBlob** code,ID3DBlob** error);
    private static void Check(HRESULT hr) { Marshal.ThrowExceptionForHR(hr); }
    private static void Release<T>(ref T* pointer) where T:unmanaged { if(pointer!=null) { ((IUnknown*)pointer)->Release();pointer=null; } }
    internal ReplayGpuRenderer(nint deviceHandle,ReplayMapMesh mesh) {
        if(deviceHandle==0)throw new InvalidOperationException("游戏图形设备暂不可用。");
        try {
            Guid iid=new("db6f6ddb-ac77-4e88-8253-819df9bbf140");ID3D11Device* device=null;
            Check(((IUnknown*)deviceHandle)->QueryInterface(&iid,(void**)&device));_device=device;
            ID3D11DeviceContext* deferred=null;Check(device->CreateDeferredContext(0,&deferred));_deferred=deferred;
            ID3D11DeviceContext* immediate=null;device->GetImmediateContext(&immediate);_immediate=immediate;
            _indexCount=(uint)mesh.Indices.Length;_opaqueIndexCount=(uint)mesh.OpaqueIndexCount;
            fixed(ReplayVertex* v=mesh.Vertices) _mapVertices=Buffer((uint)(mesh.Vertices.Length*sizeof(ReplayVertex)),D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER,v);
            fixed(uint* i=mesh.Indices) _mapIndices=Buffer((uint)(mesh.Indices.Length*sizeof(uint)),D3D11_BIND_FLAG.D3D11_BIND_INDEX_BUFFER,i);
            _dynamic=Buffer(MaxDynamicVertices*16,D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER,null,true);
            _camera=Buffer(64,D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER,null,true);
            const string shader="cbuffer Camera:register(b0){row_major float4x4 ViewProjection;} struct V{float3 p:POSITION;float4 c:COLOR;};struct P{float4 p:SV_POSITION;float4 c:COLOR;};P VS(V v){P o;o.p=mul(float4(v.p,1),ViewProjection);o.c=v.c;return o;} float4 PS(P p):SV_TARGET{return p.c;}";
            ID3DBlob* vs=null;ID3DBlob* ps=null;
            try {
                vs=Compile(shader,"VS","vs_4_0");ps=Compile(shader,"PS","ps_4_0");
                ID3D11VertexShader* vert=null;Check(device->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),null,&vert));_vs=vert;
                ID3D11PixelShader* pixel=null;Check(device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),null,&pixel));_ps=pixel;
                byte* pos=stackalloc byte[] {80,79,83,73,84,73,79,78,0};byte* col=stackalloc byte[] {67,79,76,79,82,0};
                D3D11_INPUT_ELEMENT_DESC* elements=stackalloc D3D11_INPUT_ELEMENT_DESC[2];
                elements[0]=new() { SemanticName=(sbyte*)pos,Format=DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,InputSlotClass=D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA };
                elements[1]=new() { SemanticName=(sbyte*)col,Format=DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,AlignedByteOffset=12,InputSlotClass=D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA };
                ID3D11InputLayout* layout=null;Check(device->CreateInputLayout(elements,2,vs->GetBufferPointer(),vs->GetBufferSize(),&layout));_layout=layout;
            } finally { Release(ref vs);Release(ref ps); }
            D3D11_RASTERIZER_DESC raster=new() { FillMode=D3D11_FILL_MODE.D3D11_FILL_SOLID,CullMode=D3D11_CULL_MODE.D3D11_CULL_NONE,DepthClipEnable=1,MultisampleEnable=1 };
            ID3D11RasterizerState* rs=null;Check(device->CreateRasterizerState(&raster,&rs));_raster=rs;
            D3D11_DEPTH_STENCIL_DESC depth=new() { DepthEnable=1,DepthWriteMask=D3D11_DEPTH_WRITE_MASK.D3D11_DEPTH_WRITE_MASK_ALL,DepthFunc=D3D11_COMPARISON_FUNC.D3D11_COMPARISON_LESS_EQUAL };
            depth.StencilReadMask=depth.StencilWriteMask=255;
            depth.FrontFace=depth.BackFace=new() { StencilFailOp=D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP,StencilDepthFailOp=D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP,StencilPassOp=D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP,StencilFunc=D3D11_COMPARISON_FUNC.D3D11_COMPARISON_ALWAYS };
            ID3D11DepthStencilState* ds=null;Check(device->CreateDepthStencilState(&depth,&ds));_depthState=ds;
            depth.DepthWriteMask=D3D11_DEPTH_WRITE_MASK.D3D11_DEPTH_WRITE_MASK_ZERO;
            ds=null;Check(device->CreateDepthStencilState(&depth,&ds));_overlayDepth=ds;
            D3D11_BLEND_DESC blend=new();
            blend.RenderTarget[0]=new D3D11_RENDER_TARGET_BLEND_DESC { BlendEnable=1,
                SrcBlend=D3D11_BLEND.D3D11_BLEND_SRC_ALPHA,DestBlend=D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA,BlendOp=D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
                SrcBlendAlpha=D3D11_BLEND.D3D11_BLEND_ONE,DestBlendAlpha=D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA,BlendOpAlpha=D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,RenderTargetWriteMask=15 };
            ID3D11BlendState* bs=null;Check(device->CreateBlendState(&blend,&bs));_overlayBlend=bs;
        } catch { Dispose();throw; }
    }
    private static ID3DBlob* Compile(string source,string entry,string target) {
        var bytes=Encoding.UTF8.GetBytes(source);var ep=Encoding.ASCII.GetBytes(entry+"\0");var tp=Encoding.ASCII.GetBytes(target+"\0");
        ID3DBlob* code=null;ID3DBlob* errors=null;
        try {
            fixed(byte* s=bytes,e=ep,t=tp) {
                int hr=D3DCompile(s,(nuint)bytes.Length,null,null,null,e,t,0x800,0,&code,&errors);
                if(hr<0) { var message=errors==null?"3D shader compilation failed.":Marshal.PtrToStringUTF8((nint)errors->GetBufferPointer());Release(ref code);throw new InvalidOperationException(message); }
            }
            return code;
        } finally { Release(ref errors); }
    }
    private ID3D11Buffer* Buffer(uint bytes,D3D11_BIND_FLAG bind,void* data,bool dynamic=false) {
        D3D11_BUFFER_DESC desc=new() { ByteWidth=bytes,BindFlags=(uint)bind,Usage=dynamic?D3D11_USAGE.D3D11_USAGE_DYNAMIC:D3D11_USAGE.D3D11_USAGE_IMMUTABLE,CPUAccessFlags=dynamic?(uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_WRITE:0 };
        // Uninitialized constant/stream buffers must be dynamic; static map buffers are immutable.
        D3D11_SUBRESOURCE_DATA initial=new() { pSysMem=data };ID3D11Buffer* result=null;
        Check(_device->CreateBuffer(&desc,data==null?null:&initial,&result));return result;
    }
    internal void Resize(int width,int height) {
        ObjectDisposedException.ThrowIf(_disposed,this);
        width=Math.Clamp(width,64,1280);height=Math.Clamp(height,64,900);
        if(width==Width&&height==Height)return;
        _deferred->ClearState();ReleaseTargets();
        Exception? lastFailure=null;
        foreach(uint samples in new uint[] {4,2,1}) {
            if(samples>1&&!SupportsSamples(samples))continue;
            try {
                // ImGui always receives a single-sampled SRV. All 3D geometry is drawn into the multisample target first.
                D3D11_TEXTURE2D_DESC desc=new() { Width=(uint)width,Height=(uint)height,MipLevels=1,ArraySize=1,Format=DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                    SampleDesc=new() {Count=1},Usage=D3D11_USAGE.D3D11_USAGE_DEFAULT,BindFlags=(uint)(D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET|D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE) };
                ID3D11Texture2D* color=null;Check(_device->CreateTexture2D(&desc,null,&color));_color=color;
                ID3D11ShaderResourceView* view=null;Check(_device->CreateShaderResourceView((ID3D11Resource*)color,null,&view));_view=view;
                var renderTexture=color;
                if(samples>1) {
                    desc.SampleDesc.Count=samples;desc.BindFlags=(uint)D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET;
                    ID3D11Texture2D* multi=null;Check(_device->CreateTexture2D(&desc,null,&multi));_multisampleColor=multi;renderTexture=multi;
                }
                ID3D11RenderTargetView* target=null;Check(_device->CreateRenderTargetView((ID3D11Resource*)renderTexture,null,&target));_target=target;
                // Color and depth must have identical sample counts and quality levels.
                desc.SampleDesc.Count=samples;desc.Format=DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT;desc.BindFlags=(uint)D3D11_BIND_FLAG.D3D11_BIND_DEPTH_STENCIL;
                ID3D11Texture2D* depth=null;Check(_device->CreateTexture2D(&desc,null,&depth));_depth=depth;
                ID3D11DepthStencilView* dv=null;Check(_device->CreateDepthStencilView((ID3D11Resource*)depth,null,&dv));_depthView=dv;
                Width=width;Height=height;SampleCount=samples;return;
            } catch(Exception e) { lastFailure=e;ReleaseTargets(); }
        }
        throw new InvalidOperationException("三维渲染目标创建失败。",lastFailure);
    }
    private bool SupportsSamples(uint count) {
        uint color=0,depth=0;
        return _device->CheckMultisampleQualityLevels(DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,count,&color)>=0&&color>0
            &&_device->CheckMultisampleQualityLevels(DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT,count,&depth)>=0&&depth>0;
    }

    internal void Render(Matrix4x4 viewProjection,ReadOnlySpan<ReplayVertex> dynamicVertices,ReadOnlySpan<ReplayVertex> overlays) {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(_target==null)return;
        if(dynamicVertices.Length+overlays.Length>MaxDynamicVertices)throw new InvalidOperationException("回放动态图形容量超限。");
        var ctx=_deferred;ctx->ClearState();D3D11_MAPPED_SUBRESOURCE mapped;
        Check(ctx->Map((ID3D11Resource*)_camera,0,D3D11_MAP.D3D11_MAP_WRITE_DISCARD,0,&mapped));*(Matrix4x4*)mapped.pData=viewProjection;ctx->Unmap((ID3D11Resource*)_camera,0);
        if(dynamicVertices.Length+overlays.Length>0) {
            Check(ctx->Map((ID3D11Resource*)_dynamic,0,D3D11_MAP.D3D11_MAP_WRITE_DISCARD,0,&mapped));
            fixed(ReplayVertex* v=dynamicVertices) System.Buffer.MemoryCopy(v,mapped.pData,MaxDynamicVertices*16,dynamicVertices.Length*16);
            fixed(ReplayVertex* v=overlays) System.Buffer.MemoryCopy(v,(byte*)mapped.pData+dynamicVertices.Length*16,(MaxDynamicVertices-dynamicVertices.Length)*16,overlays.Length*16);ctx->Unmap((ID3D11Resource*)_dynamic,0);
        }
        float* clear=stackalloc float[] {.035f,.05f,.085f,1};ctx->ClearRenderTargetView(_target,clear);ctx->ClearDepthStencilView(_depthView,(uint)D3D11_CLEAR_FLAG.D3D11_CLEAR_DEPTH,1,0);
        var target=_target;ctx->OMSetRenderTargets(1,&target,_depthView);ctx->OMSetDepthStencilState(_depthState,0);
        D3D11_VIEWPORT viewport=new() { Width=Width,Height=Height,MinDepth=0,MaxDepth=1 };ctx->RSSetViewports(1,&viewport);ctx->RSSetState(_raster);
        ctx->IASetInputLayout(_layout);ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->VSSetShader(_vs,null,0);ctx->PSSetShader(_ps,null,0);var camera=_camera;ctx->VSSetConstantBuffers(0,1,&camera);
        uint stride=16,offset=0;var buffer=_mapVertices;ctx->IASetVertexBuffers(0,1,&buffer,&stride,&offset);ctx->IASetIndexBuffer(_mapIndices,DXGI_FORMAT.DXGI_FORMAT_R32_UINT,0);ctx->DrawIndexed(_opaqueIndexCount,0,0);
        if(dynamicVertices.Length>0) { buffer=_dynamic;ctx->IASetVertexBuffers(0,1,&buffer,&stride,&offset);ctx->Draw((uint)dynamicVertices.Length,0); }
        if(_indexCount>_opaqueIndexCount) {
            ctx->OMSetDepthStencilState(_overlayDepth,0);ctx->OMSetBlendState(_overlayBlend,null,uint.MaxValue);
            buffer=_mapVertices;ctx->IASetVertexBuffers(0,1,&buffer,&stride,&offset);
            ctx->DrawIndexed(_indexCount-_opaqueIndexCount,_opaqueIndexCount,0);
        }
        if(overlays.Length>0) {
            ctx->OMSetDepthStencilState(_overlayDepth,0);ctx->OMSetBlendState(_overlayBlend,null,uint.MaxValue);
            buffer=_dynamic;ctx->IASetVertexBuffers(0,1,&buffer,&stride,&offset);ctx->Draw((uint)overlays.Length,(uint)dynamicVertices.Length);
        }
        if(_multisampleColor!=null) {
            // Unbind the RTV before resolving. Resolve includes both opaque and translucent passes.
            ctx->OMSetRenderTargets(0,null,null);
            ctx->ResolveSubresource((ID3D11Resource*)_color,0,(ID3D11Resource*)_multisampleColor,0,DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM);
        }
        ID3D11CommandList* command=null;
        try { Check(ctx->FinishCommandList(0,&command));_immediate->ExecuteCommandList(command,1); }
        finally { Release(ref command); }
    }
    private void ReleaseTargets() { Release(ref _view);Release(ref _target);Release(ref _depthView);Release(ref _color);Release(ref _multisampleColor);Release(ref _depth);Width=Height=0;SampleCount=1; }
    public void Dispose() {
        if(_disposed)return;_disposed=true;
        if(_deferred!=null)_deferred->ClearState();ReleaseTargets();
        Release(ref _mapVertices);Release(ref _mapIndices);Release(ref _dynamic);Release(ref _camera);Release(ref _vs);Release(ref _ps);Release(ref _layout);Release(ref _raster);Release(ref _depthState);Release(ref _overlayDepth);Release(ref _overlayBlend);Release(ref _deferred);Release(ref _immediate);Release(ref _device);
    }
}
