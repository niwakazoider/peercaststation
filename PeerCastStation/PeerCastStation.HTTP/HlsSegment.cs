﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PeerCastStation.Core;

namespace PeerCastStation.HTTP
{
  class HlsSegment
  {
    private static readonly Logger logger = new Logger(typeof(HlsSegment));
    public Channel Channel { get; private set; }
    public string url { get; private set; }

    public HlsSegment(Channel channel, string url)
    {
      this.url = url;
      this.Channel = channel;

      if (Channel.ExtraMethodContainer.OfType<Segmenter>().Count() == 0)
      {
        Channel.ExtraMethodContainer.Add(new Segmenter(Channel));
      }
    }

    public int GetContentLength()
    {
      var len = 2147483647;
      var i = GetSequenceFromUrl();
      if (i >= 0)
      {
        len = getSegmenter().getSegmentData(i).Length;
      }
      return len;
    }

    public IList<SegmentData> GetSegmentInfoList()
    {
      return getSegmenter().getSegmentInfoList();
    }

    public byte[] GetSegmentData(int i)
    {
      return getSegmenter().getSegmentData(i);
    }

    private Segmenter getSegmenter()
    {
      return Channel.ExtraMethodContainer.OfType<Segmenter>().First();
    }

    private class Segmenter : PeerCastStation.Core.Channel.IContentChangeDelegate
    {
      private Channel Channel;
      private TimeSpan keyframetime = TimeSpan.Zero;
      private Content lastPacket = null;
      private int sequence = -1;
      private MemoryStream cache = new MemoryStream();
      private IList<SegmentData> segments = new List<SegmentData>();

      public Segmenter(Channel Channel)
      {
        this.Channel = Channel;
      }

      void PeerCastStation.Core.Channel.IContentChangeDelegate.OnContentChanged()
      {
        IList<Content> contents;

        if (Channel.ContentHeader == null) return;
        if (lastPacket == null)
        {
          contents = Channel.Contents.GetNewerContents(Channel.ContentHeader.Stream, Channel.ContentHeader.Timestamp, Channel.ContentHeader.Position);
        }
        else
        {
          contents = Channel.Contents.GetNewerContents(lastPacket.Stream, lastPacket.Timestamp, lastPacket.Position);
        }
        foreach (var c in contents)
        {
          OnContentData(c);
          lastPacket = c;
        }
      }

      public byte[] getSegmentData(int i)
      {
        foreach (var c in segments)
        {
          if (c.sequence == i)
          {
            return c.data;
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

      private void OnContentData(Content c)
      {
        byte[] bytes = new byte[188];

        for (int j = 0; j < c.Data.Length; j += 188)
        {
          Array.Copy(c.Data, j, bytes, 0, 188);
          var packet = new TsPacket(bytes);
          if (packet.keyframe)
          {
            if (sequence >= 0)
            {
              addSegment(sequence, c.Timestamp - keyframetime, getCache());
              logger.Debug("add:" + SegmentIndexToString(getSegmentInfoList()));
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

    }

    public int GetSequenceFromUrl()
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
