// PeerCastStation, a P2P streaming servent.
// Copyright (C) 2014 @niwakazoider
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
// along with this program.  If not, see <http://www.gnu.org/licenses/>.using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;

namespace PeerCastStation.TS
{
  class HTTP
  {
    private static int MAX_CACHE_SIZE = 8 * 1024 * 1024;
    private static int BUFFER_SIZE = 1024;

    public static byte[] Get(string sURL)
    {

      WebRequest wrGETURL;
      Stream objStream = null;
      StringBuilder sBuilder = new StringBuilder();
      MemoryStream mStream = new MemoryStream();
      byte[] buffer = new byte[BUFFER_SIZE];
      int bytesRead = 0;

      try
      {
        wrGETURL = WebRequest.Create(sURL);
        objStream = wrGETURL.GetResponse().GetResponseStream();

        while ((bytesRead = objStream.Read(buffer, 0, buffer.Length)) != 0)
        {
          if (mStream.Length < MAX_CACHE_SIZE)
          {
            mStream.Write(buffer, 0, bytesRead);
          }
        }
      }
      catch (WebException ex)
      {
        throw ex;
      }
      finally
      {
        if (objStream != null)
          objStream.Close();
      }

      mStream.Close();

      return mStream.ToArray();
    }
  }
}
