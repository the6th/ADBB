﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ADBB
{
    public partial class MainForm : Form
    {
        private List<PackageData> dispData;
        private AdbCommand _adb;

        private Device _targetDevice;
        private PackageData _selectPackage;
        private Progress<AdbCommand.AdbProgressData> progress;

        public MainForm()
        {
            InitializeComponent();

            Console.WriteLine($"adb path is {Properties.Settings.Default.adbPath}");
            Console.WriteLine($"mldb path is {Properties.Settings.Default.mldbPath}");


            _adb = new AdbCommand(Properties.Settings.Default.adbPath, Properties.Settings.Default.mldbPath);

            //ADB.exeへのパスが設定されたら作り直し
            Properties.Settings.Default.PropertyChanged += (sender, args) =>
            {
                _adb = new AdbCommand(Properties.Settings.Default.adbPath, Properties.Settings.Default.mldbPath);
            };

            Observable.FromEventPattern(textBox1, "TextChanged")
                .Select(pattern => textBox1.Text)
                .DistinctUntilChanged()
                .Throttle(TimeSpan.FromSeconds(0.5f))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(FilterPackage);

            packageDataGrid.CellContextMenuStripNeeded += PackageDataGridCellContextMenuStripNeeded;

            progress = new Progress<AdbCommand.AdbProgressData>(async data =>
            {
                if (data.IsRequireDialog)
                {
                    if (data.IsError)
                    {
                        MessageBox.Show(data.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    if (data.IsSuccess)
                    {
                        MessageBox.Show(data.Message, "Success!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }

                toolStripProgressBar1.Visible = true;
                toolStripStatusLabel1.Visible = true;
                toolStripStatusLabel1.Text = data.Message;
                toolStripProgressBar1.MarqueeAnimationSpeed = 50;
                toolStripProgressBar1.Style = ProgressBarStyle.Marquee;

                if (data.IsSuccess | data.IsError)
                {
                    await Task.Delay(2000);
                    toolStripProgressBar1.Visible = false;
                    toolStripStatusLabel1.Visible = false;
                }
            });
        }

        /// <summary>
        /// Deviceが選択されている時のみ有効になるメニューの更新処理
        /// </summary>
        private void UpdateToolbarMenuEnable()
        {
            iPConnectToolStripMenuItem.Enabled = _targetDevice != null;
            apkInstallToolStripMenuItem.Enabled = _targetDevice != null;
            DeviceToolStripMenuItem.Enabled = _targetDevice != null;
        }

        private void PackageDataGridCellContextMenuStripNeeded(object sender, DataGridViewCellContextMenuStripNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= packageDataGrid.RowCount) return;

            var dataRow = packageDataGrid.Rows[e.RowIndex];
            dataRow.Cells[0].Selected = true;
            _selectPackage = dataRow.DataBoundItem as PackageData;
            e.ContextMenuStrip = this.PackageCellContextMenuStrip;
        }

        private void FilterPackage(string text)
        {
            if (dispData == null || dispData.Any() == false) return;
            packageDataGrid.DataSource = dispData.Where(data => data.Name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            //if (true)
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.adbPath) && string.IsNullOrWhiteSpace(Properties.Settings.Default.adbPath))
            {
                MessageBox.Show("Setup SDK path \r\n[Settings] > [Path to ADB]\r\n      or \r\n[Settings] > [Path to MLDB]", "Welcome!!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.adbPath))
                pathToADBToolStripMenuItem.Text = $"Path to ADB: ({Properties.Settings.Default.adbPath})";
            else
                pathToADBToolStripMenuItem.Text = $"Path to ADB: (none)";

            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.mldbPath))
                pathToMLDBToolStripMenuItem.Text = $"Path to MLDB: ({Properties.Settings.Default.mldbPath})";
            else
                pathToMLDBToolStripMenuItem.Text = $"Path to MLDB: (none)";

            UpdateDeviceList();
        }

        private async void uninstallToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show($"Are you sure uninstall [{_selectPackage.Name}]?", "Confirm", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (result == DialogResult.Cancel) return;
            var unInstallResult = await _adb.UnInstallPackage(_targetDevice, _selectPackage, progress);
            if (unInstallResult == false) return;
            await UpdatePackageList();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            _targetDevice = comboBox1.SelectedItem as Device;
            UpdateToolbarMenuEnable();
            UpdatePackageList();
        }

        private void 起動ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _adb.LunchPackage(_targetDevice, _selectPackage, progress);
        }

        private void DeviceUpdateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UpdateDeviceList();
        }

        private async void IpConnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var result = await _adb.ConnectIp(_targetDevice, progress);
            if (result == false) return;
            await Task.Delay(1000);//IP接続した直後はUSB接続が出てこないときがある
            await UpdateDeviceList();
        }

        private async void IpDisconnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var result = await _adb.DisconnectIp(progress);
            if (result == false) return;
            await UpdateDeviceList();
        }

        private void DataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= packageDataGrid.RowCount) return;

            var dataRow = packageDataGrid.Rows[e.RowIndex];
            dataRow.Cells[0].Selected = true;
            _selectPackage = dataRow.DataBoundItem as PackageData;

            var result = MessageBox.Show($"Are you sure you want to start [{_selectPackage.Name}]?", "Confirm", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (result == DialogResult.Cancel) return;

            _adb.LunchPackage(_targetDevice, _selectPackage, progress);
        }

        private async void APKInstallToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog;
            if (_adb.type == DEVICETYPE.MAGICLEAP)
            {
                openFileDialog = new OpenFileDialog
                {
                    Filter = "MagicLeap Application|*.mpk",
                    Title = "Select mpk to install"
                };
            }
            else
            {
                openFileDialog = new OpenFileDialog
                {
                    Filter = "Android Application|*.apk",
                    Title = "Select apk to install"
                };
            }

            var result = openFileDialog.ShowDialog();

            if (result != DialogResult.OK) return;

            var installResult = await _adb.InstallPackage(_targetDevice, openFileDialog.FileName, progress);
            if (installResult == false) return;
            await UpdatePackageList();
        }

        private void DeviceShutdownToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            var result = MessageBox.Show($"Shutdown the device:{_targetDevice.Name}？", "Shutdown", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel) return;
            _adb.Shutdown(_targetDevice, progress);
        }

        private void DeviceRebootToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show($"Reboot the device: {_targetDevice.Name}?", "Reboot", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel) return;
            _adb.Reboot(_targetDevice, progress);
        }


        private void pathToADBToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "adb.exe|adb.exe",
                Title = "Select the location to adb.exe"
            };
            var result = openFileDialog.ShowDialog();

            if (result != DialogResult.OK) return;

            Properties.Settings.Default.adbPath = openFileDialog.FileName;
            Properties.Settings.Default.Save();
        }

        private void pathToMLDBToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "mldb.exe|mldb.exe",
                Title = "Select the location to mldb.exe"
            };
            var result = openFileDialog.ShowDialog();

            if (result != DialogResult.OK) return;

            Properties.Settings.Default.mldbPath = openFileDialog.FileName;
            Console.WriteLine($"set mldb to {Properties.Settings.Default.mldbPath} / {openFileDialog.FileName}");
            Properties.Settings.Default.Save();
        }

        private void abortToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            _adb.StopPackage(_targetDevice, _selectPackage, progress);
        }

        private async Task<bool> UpdatePackageList()
        {
            var package = await _adb.GetPackageList(_targetDevice, progress);
            if (package == null) return false;
            dispData = package.ToList();
            packageDataGrid.DataSource = dispData;
            return true;
        }

        private async Task<bool> UpdateDeviceList()
        {
            packageDataGrid.DataSource = new List<PackageData>();
            comboBox1.DataSource = new List<Device>();
            var result = await _adb.GetDeviceList(progress);

            this.Text = $"ADBB ({_adb.type.ToString()})";

            if (result?.Any() != true)
            {
                MessageBox.Show("Device(s) not found", "Connect a device", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                _targetDevice = null;
                UpdateToolbarMenuEnable();
                return false;
            }

            comboBox1.DataSource = result.ToList();
            return true;
        }

        private async void PackageDataGrid_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop, false);
            foreach (var file in files)
            {
                await _adb.InstallPackage(_targetDevice, file, progress);
            }
            await UpdatePackageList();
        }

        private void PackageDataGrid_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.All;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void APKToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_adb.type == DEVICETYPE.MAGICLEAP)
            {
                MessageBox.Show("Not supported on MagicLeap ", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
            else
            {
                var saveFileDialog = new SaveFileDialog()
                {
                    AddExtension = true,
                    DefaultExt = "apk",
                    Title = "Select the location to download apk",
                    Filter = "Android APK|*.apk"

                };

                var result = saveFileDialog.ShowDialog();

                if (result != DialogResult.OK) return;
                _adb.DownloadApk(_targetDevice, _selectPackage, saveFileDialog.FileName, progress);
            }
        }

        private void DeviceToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }
    }
}
