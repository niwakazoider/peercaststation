using System;
using System.Linq;
using System.IO;
using PeerCastStation.Core;
using System.Threading;
using System.Collections.Generic;

namespace PeerCastStation.TS
{
  internal class BadDataException : ApplicationException
  {
  }

  public class TSContentReader
    : IContentReader
  {
    public TSContentReader(Channel channel)
    {
      this.Channel = channel;
    }

    private int streamIndex = -1;
    private DateTime streamOrigin;
    private int packetSize = -1;
    private byte[] cache = new byte[0];

    public ParsedContent Read(Stream stream)
    {
      if (stream.Length - stream.Position <= 0) throw new EndOfStreamException();
      var res = new ParsedContent();
      var pos = Channel.ContentPosition;
      if (Channel.ContentHeader == null){
        streamIndex = Channel.GenerateStreamID();
        streamOrigin = DateTime.Now;
        res.ContentHeader = new Content(streamIndex, TimeSpan.Zero, pos, new byte[] { });
        var channel_info = new AtomCollection(Channel.ChannelInfo.Extra);
        channel_info.SetChanInfoType("TS");
        channel_info.SetChanInfoStreamType("video/mpeg");
        channel_info.SetChanInfoStreamExt(".ts");
        res.ChannelInfo = new ChannelInfo(channel_info);
      }
      res.Contents = new List<Content>();

      var eos = false;
      while (!eos){
        var start_pos = stream.Position;
        try{
          if (packetSize < 0){
            packetSize = getPacketSize(stream);
          }
          var bytes = ReadPacket(stream, packetSize);
          var packet = new TsPacket(bytes);
          if (packet.payload_unit_start_indicator > 0){
            if (cache.Length > 1024){
              addContent(res, cache, pos);
              clearCache();
            }
          }
          addCache(bytes);

          pos += bytes.Length;
        }
        catch (EndOfStreamException){
          stream.Position = start_pos;
          eos = true;
        }
        catch (BadDataException){
          stream.Position = start_pos + 1;
        }
      }

      return res;
    }

    private void addContent(ParsedContent res, byte[] bytes, long pos)
    {
      if (bytes.Length > 0){
        res.Contents.Add(new Content(streamIndex, DateTime.Now - streamOrigin, pos, bytes));
      }
    }

    private void clearCache()
    {
      cache = new byte[0];
    }

    private void addCache(byte[] bytes)
    {
      cache = cache.Concat(bytes).ToArray();
      if (cache.Length > 8 * 1024 * 1024){
        cache = new byte[0];
      }
    }

    private int getPacketSize(Stream stream)
    {
      var position = stream.Position;

      var packetSize = 9024;
      var bytes = ReadBytes(stream, 5);
      if (isSyncByte(bytes[0])){
        packetSize = 188;
      }
      else if (isSyncByte(bytes[4])){
        packetSize = 192;
      }
      else{
        throw new BadDataException();
      }

      stream.Position = position;

      return packetSize;
    }

    private byte[] ReadPacket(Stream stream, int packetSize)
    {
      var bytes = new byte[188];
      var temp = ReadBytes(stream, packetSize);
      if (packetSize == 192){
        Array.Copy(temp, 4, bytes, 0, packetSize - 4);
      }
      else{
        bytes = temp;
      }
      return bytes;
    }

    private static byte[] ReadBytes(Stream stream, int len)
    {
      var res = new byte[len];
      if (stream.Read(res, 0, len) < len){
        throw new EndOfStreamException();
      }
      return res;
    }

    private Boolean isSyncByte(int b)
    {
      return b == 0x47;
    }

    private class TsPacket
    {
      public int sync_byte { get; private set; }
      public int payload_unit_start_indicator { get; private set; }

      public TsPacket(byte[] packet)
      {
        this.sync_byte = packet[0];
        this.payload_unit_start_indicator = (packet[1] & 0x40) >> 6;
      }
    }

    public string Name { get { return "TS"; } }
    public Channel Channel { get; private set; }
  }

  public class TSContentReaderFactory
    : IContentReaderFactory
  {
    public string Name { get { return "MpegTS (TS)"; } }

    public IContentReader Create(Channel channel)
    {
      return new TSContentReader(channel);
    }
  }

  [Plugin]
  public class TSContentReaderPlugin
    : PluginBase
  {
    override public string Name { get { return "TS Content Reader"; } }

    private TSContentReaderFactory factory;
    override protected void OnAttach()
    {
      if (factory == null) factory = new TSContentReaderFactory();
      Application.PeerCast.ContentReaderFactories.Add(factory);
    }

    override protected void OnDetach()
    {
      Application.PeerCast.ContentReaderFactories.Remove(factory);
    }
  }
}
