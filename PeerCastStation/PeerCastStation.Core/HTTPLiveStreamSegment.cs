using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PeerCastStation.Core
{
  public class HTTPLivestreamSegment: IContentSink
  {
    private static readonly Logger logger = new Logger(typeof(HTTPLivestreamSegment));
    public Channel Channel { get; private set; }
    private Segmenter segmenter = null;
    private IContentSink sink = null;

    public HTTPLivestreamSegment(Channel channel)
    {
      this.Channel = channel;
      this.segmenter = new Segmenter(Channel);
      var filter = Channel.PeerCast.ContentFilters.FirstOrDefault(f => f.Name.ToLowerInvariant()=="FLVToTS".ToLowerInvariant());
      if(filter!=null) {
        sink = filter.Activate(this);
        Channel.AddContentSink(sink);
      }
    }

    public IList<SegmentData> GetSegmentInfoList()
    {
      return segmenter.getSegmentInfoList();
    }

    public byte[] GetSegmentData(int i)
    {
      return segmenter.getSegmentData(i).ToArray();
    }

    public Segmenter GetSegmenter()
    {
      return segmenter;
    }

    public class Segmenter
    {
      private Channel Channel;
      private TimeSpan keyframetime = TimeSpan.Zero;
      private Content headerContent = null;
      private int sequence = -1;
      private MemoryStream cache = new MemoryStream();
      private IList<SegmentData> segments = new List<SegmentData>();

      public Segmenter(Channel Channel)
      {
        this.Channel = Channel;
      }

      public byte[] getSegmentData(int i)
      {
        foreach (var c in segments)
        {
          if (c.sequence == i)
          {
            if(headerContent!=null) {
              return headerContent.Data.Concat(c.data).ToArray();
            }
            else {
              return c.data;
            }
          }
        }
        return new byte[0];
      }

      public IList<SegmentData> getSegmentInfoList()
      {
        var list = new List<SegmentData>();
        foreach (var c in segments)
        {
          list.Add(c);
        }
        if (list.Count > 3)
        {
          list = list.GetRange(list.Count - 3, 3);
        }
        return list;
      }

      public void addSegment(int sequence, TimeSpan duration, byte[] data)
      {
        var durationsec = (int) duration.TotalSeconds;
        if (durationsec < 1) durationsec = 1;
        segments.Add(new SegmentData(sequence, durationsec, data));
        if (segments.Count > 7)
        {
          segments.RemoveAt(0);
        }
      }
      public void addCache(byte[] bytes)
      {
        if (cache.Length < 8 * 1024 * 1024)
        {
          cache.Write(bytes, 0, bytes.Length);
        }
        else
        {
          //cache over flow
        }
      }

      public byte[] getCache()
      {
        cache.Close();
        byte[] bytes = cache.ToArray();
        cache = new MemoryStream();
        return bytes;
      }

      public void clearCache()
      {
        cache.Close();
        cache = new MemoryStream();
      }

      public void OnContentData(Content c)
      {
        var contentType = Channel.ChannelInfo.ContentType;
        if(contentType!="TS" && contentType!="FLV") {
          return;
        }

        byte[] bytes = new byte[188];

        for (int j = 0; j < c.Data.Length; j += 188)
        {
          Array.Copy(c.Data, j, bytes, 0, 188);
          var packet = new TSPacket(bytes);
          if (packet.keyframe)
          {
            if (sequence >= 0)
            {
              addSegment(sequence, c.Timestamp - keyframetime, getCache());
              logger.Debug("update segments:" + SegmentIndexToString(getSegmentInfoList()));
              //logger.Debug(BitConverter.ToString(bytes).Replace("-", string.Empty));
            }
            sequence++;
            keyframetime = c.Timestamp;
          }
          if (sequence >= 0)
          {
            addCache(bytes);
          }
        }

      }

      public void OnContentHeader(Content content_header)
      {
        var contentType = Channel.ChannelInfo.ContentType;
        if(contentType!="TS" && contentType!="FLV") {
          return;
        }
        headerContent = content_header;
        OnContentData(content_header);
      }
    }

    public int GetSequenceFromUrl(string url)
    {
      var seg = url.Substring(url.LastIndexOf("_") + 1, 5);
      if (seg != "http:")
      {
        return int.Parse(seg);
      }
      else
      {
        return -1;
      }
    }

    public static String SegmentIndexToString(IList<SegmentData> list)
    {
      String s = "";
      for (int i = 0; i < list.Count; i++)
      {
        s = s + " " + list[i].sequence;
      }
      return "[" + s.Trim() + "]";
    }

    public void OnChannelInfo(ChannelInfo channel_info)
    {
    }

    public void OnChannelTrack(ChannelTrack channel_track)
    {
    }

    public void OnContentHeader(Content content_header)
    {
      segmenter.OnContentHeader(content_header);
    }

    public void OnContent(Content content)
    {
      segmenter.OnContentData(content);
    }
    
    public void OnStop(StopReason reason)
    {
      if(sink!=null) {
        Channel.RemoveContentSink(sink);
      }
    }
  }

  public class SegmentData
  {
    public int sequence;
    public int duration;
    public byte[] data;
    public SegmentData(int seq, int duration, byte[] data)
    {
      this.sequence = seq;
      this.duration = duration;
      this.data = data;
    }
  }
}