using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ADBB
{
    public enum DEVICETYPE
    {
        UNKNOWN,
        ANDROID,
        MAGICLEAP
    }

    /// <summary>
    /// AdbCommand実行用クラス
    /// </summary>
    public class AdbCommand
    {
        public DEVICETYPE type = DEVICETYPE.UNKNOWN;

        /// <summary>
        /// 非同期実行中進捗コールバック用データ
        /// </summary>
        public class AdbProgressData
        {
            public string Message { get; }
            public bool IsError { get; }
            public bool IsSuccess { get; }
            public bool IsRequireDialog { get; }
            public Exception Ex { get; }

            public AdbProgressData(string message, bool isError = false, bool isSuccess = false, bool? isRequireDialog = null)
            {
                Message = message;
                IsError = isError;
                IsSuccess = isSuccess;
                IsRequireDialog = isRequireDialog ?? isError;   //エラーの場合は大抵ダイアログ表示する
            }
            public AdbProgressData(string message, Exception ex)
            {
                Message = message + ":" + ex.Message;
                Ex = ex;
                IsError = true;
                IsRequireDialog = true;   //エラーの場合は大抵ダイアログ表示する
            }
        }

        /// <summary>
        /// ADBコマンドへのパス
        /// </summary>
        public string AdbCommandPath { get; }

        /// <summary>
        /// MLDBコマンドへのパス
        /// </summary>
        public string MldbCommandPath { get; }

        public AdbCommand(string adbCommandPath, string mldbCommandPath = "")
        {
            AdbCommandPath = adbCommandPath;
            MldbCommandPath = mldbCommandPath;
        }

        /// <summary>
        /// キャンセル指定なしMLDBコマンド起動
        /// </summary>
        /// <param name="device"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private Task<string[]> MldbCmd(Device device, params string[] args)
        {
            return MldbCmd(CancellationToken.None, device, args);
        }


        /// <summary>
        /// キャンセル可能ADBコマンド起動
        /// 標準出力結果を行ごとの配列にして返却
        /// </summary>
        /// <param name="ct"></param>
        /// <param name="device"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private Task<string[]> MldbCmd(CancellationToken ct, Device device, params string[] args)
        {
            if (device == null) throw new Exception("Device is not selected.");
            if (device.Type == "unauthorized") throw new Exception("Permission denied.please set permission on your MagicLeap.");

            var tcs = new TaskCompletionSource<string[]>();
            Task.Run(() =>
            {
                var pro = new Process();

                if (ct != CancellationToken.None)
                {
                    ct.Register(() =>
                    {
                        tcs.TrySetCanceled(ct);
                        try
                        {
                            pro.Kill();
                            pro.Dispose();
                        }
                        catch
                        {

                        }
                    });
                }

                var app = pro.StartInfo;
                app.FileName = MldbCommandPath ?? "mldb";

                app.Arguments = string.Join(" ", args);

                if (device.Name != "None")
                {
                    app.Arguments = $"-s {device.Name} " + app.Arguments;
                }

                Console.WriteLine($"MldbCmd: {app.FileName} {app.Arguments}");

                app.CreateNoWindow = true;
                app.UseShellExecute = false;
                app.RedirectStandardOutput = true;

                pro.Start();

                var output = pro.StandardOutput.ReadToEnd();

                tcs.TrySetResult(output.Replace("\r", "").Split(new[]{
                    "\n"
                }, StringSplitOptions.RemoveEmptyEntries));
            }, ct);

            return tcs.Task;
        }

        /// <summary>
        /// キャンセル指定なしADBコマンド起動
        /// </summary>
        /// <param name="device"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private Task<string[]> AdbCmd(Device device, params string[] args)
        {
            if (type == DEVICETYPE.MAGICLEAP)
                return MldbCmd(CancellationToken.None, device, args);
            else
                return AdbCmd(CancellationToken.None, device, args);

        }

        /// <summary>
        /// キャンセル可能ADBコマンド起動
        /// 標準出力結果を行ごとの配列にして返却
        /// </summary>
        /// <param name="ct"></param>
        /// <param name="device"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private Task<string[]> AdbCmd(CancellationToken ct, Device device, params string[] args)
        {
            if (device == null) throw new Exception("Device is not selected.");
            if (device.Type == "unauthorized") throw new Exception("Permission denied.please set permission on your Android.");

            var tcs = new TaskCompletionSource<string[]>();
            Task.Run(() =>
            {
                var pro = new Process();

                if (ct != CancellationToken.None)
                {
                    ct.Register(() =>
                    {
                        tcs.TrySetCanceled(ct);
                        try
                        {
                            pro.Kill();
                            pro.Dispose();
                        }
                        catch
                        {

                        }
                    });
                }

                var app = pro.StartInfo;
                app.FileName = AdbCommandPath ?? "adb";
                Console.WriteLine($"Android app.FileName  =  {app.FileName}");

                app.Arguments = string.Join(" ", args);

                if (device != Device.None)
                {
                    app.Arguments = $"-s {device.Name} " + app.Arguments;
                }
                Console.WriteLine($"adbCmd: {app.FileName} {app.Arguments}");

                app.CreateNoWindow = true;
                app.UseShellExecute = false;
                app.RedirectStandardOutput = true;

                pro.Start();

                var output = pro.StandardOutput.ReadToEnd();

                tcs.TrySetResult(output.Replace("\r", "").Split(new[]{
                    "\n"
                }, StringSplitOptions.RemoveEmptyEntries));
            }, ct);

            return tcs.Task;
        }

        /// <summary>
        /// 接続されているAndroidデバイス一覧取得
        /// </summary>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<IEnumerable<Device>> GetDeviceList(IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Get device list", progress, async () =>
            {
                type = DEVICETYPE.UNKNOWN;

                Console.WriteLine($"type {type.ToString()}");
                var result = await AdbCmd(Device.None, "devices");
                //AndroidDeviceが見つかった場合
                if (result.Length > 1)
                {
                    //if (type == DEVICETYPE.UNKNOWN)
                    type = DEVICETYPE.ANDROID;
                }
                else
                {
                    result = await MldbCmd(Device.None, "devices");

                    if (result.Length > 1)
                    {
                        type = DEVICETYPE.MAGICLEAP;
                    }
                }
                return result.Skip(1).Select(s => s.Split(new[] { "\t" }, StringSplitOptions.None)).Select(s => new Device(s[0], s[1]));

            });
        }

        /// <summary>
        /// 選択したAndroidデバイスにインストールされたサードパーティー製アプリのパッケージ名一覧取得
        /// </summary>
        /// <param name="device"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<IEnumerable<PackageData>> GetPackageList(Device device, IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Get Package list", progress, async () =>
            {
                string[] result;

                if (type == DEVICETYPE.MAGICLEAP)
                {
                    result = await MldbCmd(device, "packages");
                    return result.Skip(1).Where(s => !(s.Contains("com.magicleap"))).Select(s => s.Split(new[] { " " }, StringSplitOptions.None)).Select(s => new PackageData(s[0], s[1]));
                }
                else
                {
                    result = await AdbCmd(device, "shell pm list package", "-3", "-f");
                    return result.Skip(0).Select(s => s.Split(new[] { ":", "=" }, StringSplitOptions.None)).Select(s => new PackageData(s[2], s[1]));
                }
            });
        }

        /// <summary>
        /// 指定したAndroidデバイスから指定したパッケージ名のアプリのアンインストール
        /// </summary>
        /// <param name="device"></param>
        /// <param name="package"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<bool> UnInstallPackage(Device device, PackageData package, IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Uninstalling..", progress, async () =>
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    string[] result;
                    bool isSuccess = false;
                    if (type == DEVICETYPE.MAGICLEAP)
                    {
                        result = await MldbCmd(device, "uninstall", package.Name);
                        isSuccess = result.LastOrDefault()?.IndexOf("Successfully ") >= 0;
                    }
                    else
                    {
                        result = await AdbCmd(cts.Token, device, "uninstall", package.Name);
                        isSuccess = result.FirstOrDefault()?.IndexOf("Success") >= 0;
                    }
                    if (isSuccess) return true;
                    throw new Exception();
                }
            });
        }

        /// <summary>
        /// 指定したAndroidデバイスから、指定したパッケージ名のアプリの起動
        /// </summary>
        /// <param name="device"></param>
        /// <param name="package"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<bool> LunchPackage(Device device, PackageData package, IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Launch App", progress, async () =>
            {
                string[] result;
                bool isSuccess = false;
                if (type == DEVICETYPE.MAGICLEAP)
                {
                    result = await MldbCmd(device, "launch", package.Name);
                    isSuccess = result.LastOrDefault()?.IndexOf("Success") >= 0;
                }
                else
                {
                    var dumpResult = await AdbCmd(device, "shell", $"\"pm dump {package.Name} | grep -A 2 android.intent.action.MAIN | head -2 | tail -1\"");
                    var packageActivityName = dumpResult[1].Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
                    result = await AdbCmd(device, "shell am start", "-n", packageActivityName);
                    isSuccess = result.FirstOrDefault()?.IndexOf("Success") >= 0 || result.FirstOrDefault()?.IndexOf("Starting") >= 0;
                }
                if (isSuccess) return true;
                throw new Exception();
            });
        }

        /// <summary>
        /// 指定したAndroidデバイスから、指定したパッケージ名のアプリの強制終了を試行
        /// </summary>
        /// <param name="device"></param>
        /// <param name="package"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<bool> StopPackage(Device device, PackageData package, IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Terminate App", progress, async () =>
            {
                string[] result;
                if (type == DEVICETYPE.MAGICLEAP)
                {
                    result = await MldbCmd(device, "terminate", package.Name);
                }
                else
                {
                    result = await AdbCmd(device, "shell am force-stop", package.Name);
                }
                return true;
            });
        }

        /// <summary>
        /// 指定したAndroidデバイスから、指定したパッケージ名のAPKファイルをダウンロー</summary>
        /// <param name="device"></param>
        /// <param name="package"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<bool> DownloadApk(Device device, PackageData package, string path, IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Download Apk..", progress, async () =>
            {
                string[] result;
                bool isSuccess = false;
                if (type == DEVICETYPE.MAGICLEAP)
                {
                    //MagicLeapは未対応
                    return false;
                    throw new Exception();
                }
                else
                {
                    result = await AdbCmd(device, "pull", package.ApkPath, path);
                    isSuccess = result.LastOrDefault()?.IndexOf("pulled") >= 0;
                }
                if (isSuccess) return true;
                throw new Exception();
            });
        }

        /// <summary>
        /// 指定したAndroidデバイスに、filePathのAPKファイルをインストール
        /// </summary>
        /// <param name="device"></param>
        /// <param name="filePath"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<bool> InstallPackage(Device device, string filePath, IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Install..", progress, async () =>
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
                {
                    string[] result;
                    bool isSuccess = false;
                    if (type == DEVICETYPE.MAGICLEAP)
                    {
                        result = await MldbCmd(cts.Token, device, "install", "-u", filePath);
                        isSuccess = result.Any(s => s.IndexOf("Successfully") >= 0);
                    }
                    else
                    {
                        result = await AdbCmd(cts.Token, device, "install", "-r", filePath);
                        isSuccess = result.Any(s => s.IndexOf("Success") >= 0);
                    }
                    if (isSuccess) return true;
                    throw new Exception();
                }
            }, true);
        }

        /// <summary>
        /// 指定したAndroidデバイスに、IPアドレスによる接続を試行
        /// </summary>
        /// <param name="device"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<bool> ConnectIp(Device device, IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Connect the device using TCP", progress, async () =>
            {
                string[] result;

                if (type == DEVICETYPE.MAGICLEAP)
                {
                    result = await AdbCmd(device, "wifi status");
                    var res = result.Select(s => s.Split(new[] { "IpAddr=", " " }, StringSplitOptions.None));

                    string[] str = Regex.Split(result[0], @".*IpAddr=([0-9\.]{4,})\s.*");

                    result[0] = str[1];
                    //Console.WriteLine("XXX:" + result[0]);
                    await Task.Delay(1000);//IP接続した直後はUSB接続が出てこないときがある

                    await MldbCmd(Device.None, "tcpip", "-p", "5555");
                    await Task.Delay(1000);//IP接続した直後はUSB接続が出てこないときがある

                    var connectResult = await MldbCmd(device, "connect", result[0]);
                    Console.WriteLine("connectResult:" + connectResult);

                    //foreach (var s in connectResult)
                    //{
                    //    Console.WriteLine(s);
                    //}

                    if (connectResult.Any(s => s.IndexOf("connected to") >= 0)) return true;
                }
                else
                {
                    result = await AdbCmd(device, "shell", "\"ifconfig wlan0 | grep 'inet addr:' | sed -e 's/^.*inet addr://' -e 's/ .*//'\"");
                    await AdbCmd(device, "tcpip", "5555");
                    var connectResult = await AdbCmd(device, "connect", $"{result[0]}:5555");
                    if (connectResult.Any(s => s.IndexOf("connected to") >= 0)) return true;
                }
                throw new Exception();
            });
        }

        /// <summary>
        /// IPアドレスによる接続を切断
        /// </summary>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<bool> DisconnectIp(IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Disconnect TCP", progress, async () =>
            {
                await AdbCmd(Device.None, "disconnect");
                return true;
            });
        }

        /// <summary>
        /// 指定したAndroidデバイスの強制シャットダウンを試行
        /// </summary>
        /// <param name="device"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<bool> Shutdown(Device device, IProgress<AdbProgressData> progress)
        {
            return ProgressWrap("Shutdown", progress, async () =>
            {
                if (type == DEVICETYPE.MAGICLEAP)
                {
                    await MldbCmd(device, "shutdown");
                }
                else
                {
                    await AdbCmd(device, "shell", "reboot -p");
                }
                return true;
            });
        }

        /// <summary>
        /// 指定したAndroidデバイスの強制再起動を試行
        /// </summary>
        /// <param name="device"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Task<bool> Reboot(Device device, Progress<AdbProgressData> progress)
        {
            return ProgressWrap("端末再起動", progress, async () =>
            {
                if (type == DEVICETYPE.MAGICLEAP)
                {
                    await MldbCmd(device, "reboot");
                }
                else
                {
                    await AdbCmd(device, "shell", "reboot");
                }
                return true;
            });
        }

        /// <summary>
        /// 進捗管理用ラップメソッド
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <param name="progress"></param>
        /// <param name="proc"></param>
        /// <returns></returns>
        private async Task<T> ProgressWrap<T>(string name, IProgress<AdbProgressData> progress, Func<Task<T>> proc, bool isRequireCompleteDialog = false)
        {
            try
            {
                progress.Report(new AdbProgressData(name + "Start.."));
                var result = await proc.Invoke();
                progress.Report(new AdbProgressData(name + "Success", false, true, isRequireCompleteDialog));
                return result;
            }
            catch (Exception ex)
            {
                progress.Report(new AdbProgressData(name + "Failed", ex));
            }
            return default(T);
        }
    }
}