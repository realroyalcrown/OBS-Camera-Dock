using System;

namespace CameraDock {
    static class ObsDevice {
        // OBS win-dshow encode-dstr.hpp: encoded name + ':' + encoded device path.
        // Decode in OBS order so literal escape-like text is not decoded twice.
        public static bool Matches(string encodedId,string devicePath) {
            if(String.IsNullOrEmpty(encodedId)||String.IsNullOrEmpty(devicePath)) return false;
            int separator=encodedId.IndexOf(':');
            if(separator<0) return false;
            string path=encodedId.Substring(separator+1).Replace("#3A",":").Replace("#22","#");
            return String.Equals(path,devicePath,StringComparison.OrdinalIgnoreCase);
        }
    }
}
