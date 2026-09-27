// Build-time only. Decode upstream AAC using Windows' own Media Foundation.
// The shipped client plays PCM directly; no decoder or build tool is required.
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>
#include <fstream>
#include <vector>
#include <stdexcept>
#include <iostream>
using Microsoft::WRL::ComPtr;
static void check(HRESULT result){if(FAILED(result))throw result;}
int wmain(int argc,wchar_t** argv){
    if(argc!=3)return 2;HRESULT result=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(result))return 3;
    if(FAILED(MFStartup(MF_VERSION))){CoUninitialize();return 3;}int code=0;
    try{
        ComPtr<IMFSourceReader> reader;check(MFCreateSourceReaderFromURL(argv[1],nullptr,&reader));
        check(reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS,FALSE));check(reader->SetStreamSelection(MF_SOURCE_READER_FIRST_AUDIO_STREAM,TRUE));
        ComPtr<IMFMediaType> type;check(MFCreateMediaType(&type));check(type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Audio));check(type->SetGUID(MF_MT_SUBTYPE,MFAudioFormat_PCM));check(type->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE,16));
        check(reader->SetCurrentMediaType(MF_SOURCE_READER_FIRST_AUDIO_STREAM,nullptr,type.Get()));type.Reset();check(reader->GetCurrentMediaType(MF_SOURCE_READER_FIRST_AUDIO_STREAM,&type));
        WAVEFORMATEX* format=nullptr;UINT32 length=0;check(MFCreateWaveFormatExFromMFMediaType(type.Get(),&format,&length));
        WAVEFORMATEX fmt=*format;CoTaskMemFree(format);if(fmt.wFormatTag!=WAVE_FORMAT_PCM||fmt.wBitsPerSample!=16||fmt.nChannels<1||fmt.nChannels>2)throw E_INVALIDARG;
        std::vector<BYTE> pcm;
        for(int count=0;count<10000;count++){
            ComPtr<IMFSample> sample;DWORD flags=0;check(reader->ReadSample(MF_SOURCE_READER_FIRST_AUDIO_STREAM,0,nullptr,&flags,nullptr,&sample));if(flags&MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED)throw E_INVALIDARG;
            if(sample){ComPtr<IMFMediaBuffer> buffer;check(sample->ConvertToContiguousBuffer(&buffer));BYTE* bytes=nullptr;DWORD size=0;check(buffer->Lock(&bytes,nullptr,&size));pcm.insert(pcm.end(),bytes,bytes+size);check(buffer->Unlock());if(pcm.size()>5*1024*1024)throw E_INVALIDARG;}
            if(flags&MF_SOURCE_READERF_ENDOFSTREAM)break;if(count==9999)throw E_ABORT;
        }
        if(pcm.empty()||pcm.size()>10ULL*fmt.nAvgBytesPerSec)throw E_INVALIDARG;
        DWORD riff=(DWORD)pcm.size()+36,formatSize=16,dataSize=(DWORD)pcm.size();std::ofstream out(argv[2],std::ios::binary);out.write("RIFF",4);out.write((char*)&riff,4);out.write("WAVEfmt ",8);out.write((char*)&formatSize,4);out.write((char*)&fmt,16);out.write("data",4);out.write((char*)&dataSize,4);out.write((char*)pcm.data(),pcm.size());if(!out)throw E_FAIL;
        std::cout<<"PCM16 frames="<<pcm.size()/fmt.nBlockAlign<<" rate="<<fmt.nSamplesPerSec<<" channels="<<fmt.nChannels<<"\n";
    }catch(HRESULT error){std::cerr<<"Decode failed HRESULT="<<std::hex<<error<<"\n";code=1;}catch(...){code=1;}
    MFShutdown();CoUninitialize();return code;
}
