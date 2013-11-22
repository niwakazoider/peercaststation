// PeerCastStation, a P2P streaming servent.
// Copyright (C) 2013 niwakazoider
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PeerCastStation.Core;

namespace PeerCastStation.HTTP
{
  class HTTPFlvExtend
  {
    private long startTime = -1;
    private bool keyframeFlag = false;
    private bool startTag = false;
    private MemoryStream cache = new MemoryStream();

    public HTTPFlvExtend(){
    }

    class FlvPacket
    {
      public int size { get; private set; }
      public long time { get; private set; }
      public Byte[] data { get; private set; }
      public Boolean video { get; private set; }
      public Boolean keyframe { get; private set; }
      public Boolean avc { get; private set; }

      public void read(MemoryStream stream){
        var startPosition = stream.Position;
        try{
          if (isCacheLimit(stream)) throw new OverflowException();

          if (stream.Length - stream.Position < 11) throw new EndOfStreamException();
          var header = new Byte[11];
          stream.Read(header, 0, header.Length);
          video = (header[0] == 0x09);
          size = (header[1] << 16) | (header[2] << 8) | (header[3]);
          time = (header[7] << 24) | (header[4] << 16) | (header[5] << 8) | (header[6]);

          stream.Position = startPosition;

          if (stream.Length - stream.Position < 11 + size + 4) throw new EndOfStreamException();
          data = new Byte[11 + size + 4];
          stream.Read(data, 0, data.Length);

          avc = (header[0] == 0x09 && (data[11] == 0x17 || data[11] == 0x27) && data[12] == 0x01);
          keyframe = (header[0] == 0x09 && data[11] == 0x17 && data[12] == 0x01) ? true : false;
        }
        catch (EndOfStreamException eos){
          stream.Position = startPosition;
          throw eos;
        }
      }

      private Boolean isCacheLimit(MemoryStream stream){
        if (stream.Position + 1 >= stream.Length || stream.Length > 1024 * 256){
          return true;
        }
        else{
          return false;
        }
      }

      public void setTime(long time){
        var binary = BitConverter.GetBytes(time);
        data[7] = binary[3];
        data[4] = binary[2];
        data[5] = binary[1];
        data[6] = binary[0];
      }
    }

    public byte[] parse(byte[] bytes)
    {
      Byte[] buffer = new Byte[0];

      if (!startTag){
        if (bytes[0] == 0x08 || bytes[0] == 0x09 || bytes[0] == 0x12){
          startTag = true;
        }
        else{
          return buffer;
        }
      }

      lock (this){
        var pos = cache.Position;
        cache.Position = cache.Length;
        cache.Write(bytes, 0, bytes.Length);
        cache.Position = pos;
      }

      try{
        while (true){
          var flv = new FlvPacket();
          lock (this){
            flv.read(cache);
          }

          if (flv.video && (flv.keyframe || !flv.avc)) keyframeFlag = true;
          if (flv.time > 0 && startTime < 0) startTime = flv.time;

          if (flv.time > 0){
            flv.setTime(flv.time - startTime);
          }
          if (!flv.video || keyframeFlag){
            buffer = Enumerable.Concat(buffer, flv.data).ToArray();
          }
        }
      }
      catch (OverflowException){
        lock (this){
          cache = new MemoryStream();
        }
      }
      catch (Exception){

      }

      return buffer;
    }
  }

}
