using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace UploaderContents.Controllers {

    public class UploadConfig {

        /// <summary>
        /// 视频上传的保存路径
        /// </summary>
        public static string UpLoadPath { get; set; }

        /// <summary>
        /// 临时路径,分块上传到这,之后合并到 UpLoadPath
        /// </summary>
        public static string TmpPath { get; set; }
    }

    public class UploadController : Controller {

        private readonly IWebHostEnvironment _hostingEnvironment;

        public UploadController(IWebHostEnvironment environment) {
            _hostingEnvironment = environment;
            //配置上传文件的路径
            UploadConfig.UpLoadPath = Path.Combine(_hostingEnvironment.WebRootPath, @"Upload\");
            UploadConfig.TmpPath = Path.Combine(_hostingEnvironment.WebRootPath, @"tempVideo\");

            if (!System.IO.Directory.Exists(UploadConfig.UpLoadPath)) {
                System.IO.Directory.CreateDirectory(UploadConfig.UpLoadPath);
            }

            if (!System.IO.Directory.Exists(UploadConfig.TmpPath)) {
                System.IO.Directory.CreateDirectory(UploadConfig.TmpPath);
            }
        }

        public IActionResult Index() {
            return View();
        }
        public IActionResult Image() {
            return View();
        }

        public IActionResult Video() {
            return View();
        }


        [HttpPost]
        public async Task<IActionResult> UploadingStream() {
            //获取boundary
            var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(Request.ContentType).Boundary).Value;
            //得到reader
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);
            //{ BodyLengthLimit = 2000 };//
            var len = reader.BodyLengthLimit;//test

            var section = await reader.ReadNextSectionAsync();

            //读取section
            while (section != null) {
                var hasContentDispositionHeader = ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition);
                if (hasContentDispositionHeader) {
                    var trustedFileNameForFileStorage = Path.GetRandomFileName();
                    await WriteFileAsync(section.Body, Path.Combine(UploadConfig.UpLoadPath, trustedFileNameForFileStorage));
                }
                section = await reader.ReadNextSectionAsync();
            }

            return Created(nameof(UploadController), null);
        }

        /// <summary>
        /// 写文件到磁盘
        /// </summary>
        /// <param name="stream">流</param>
        /// <param name="path">路径</param>
        /// <returns></returns>
        public static async Task<int> WriteFileAsync(System.IO.Stream stream, string path) {
            const int FILE_WRITE_SIZE = 84975;//写出缓冲区大小
            int writeCount = 0;
            using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write, FILE_WRITE_SIZE, true)) {
                byte[] byteArr = new byte[FILE_WRITE_SIZE];
                int readCount = 0;
                while ((readCount = await stream.ReadAsync(byteArr, 0, byteArr.Length)) > 0) {
                    await fileStream.WriteAsync(byteArr, 0, readCount);
                    writeCount += readCount;
                }
            }
            return writeCount;
        }

        [HttpPost]
        public async Task<IActionResult> OnPostUploadAsync(List<IFormFile> files) {
            long size = files.Sum(f => f.Length);
            foreach (var formFile in files) {
                if (formFile.Length > 0) {
                    var filePath = Path.GetTempFileName();
                    using (var stream = System.IO.File.Create(filePath)) {
                        await formFile.CopyToAsync(stream);
                    }
                }
            }
            // Process uploaded files
            // Don't rely on or trust the FileName property without validation.
            return Ok(new { count = files.Count, size });
        }



        /// <summary>
        /// 上传文件
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> FileSave() {
            var files = Request.Form.Files;
            long size = files.Sum(f => f.Length);//10MB
            foreach (var formFile in files) {
                if (formFile.Length > 0) {
                    var tempDicForFormFile = formFile.FileName.Split('.')[0];//为上传的文件创建一个文件夹1
                    string fileExt = formFile.FileName.Substring(formFile.FileName.IndexOf('.')); //文件扩展名，含“.”
                    string DirPath = Path.Combine(UploadConfig.TmpPath, tempDicForFormFile);//为上传的文件创建一个文件夹2
                    if (!System.IO.Directory.Exists(DirPath)) {
                        System.IO.Directory.CreateDirectory(DirPath);
                    }
                    //为上传的文件创建一个文件夹3 END
                    var filePath = DirPath + @"\" + Request.Form["chunk"] + fileExt;
                    using (var stream = new FileStream(filePath, FileMode.Create)) {
                        await formFile.CopyToAsync(stream);//写入文件
                    }
                }
            }

            return Ok(new { count = files.Count, size });
        }

        /// <summary>
        /// 合并请求
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> FileMerge(CancellationToken cancellationToken) {
            bool ok = false;
            string errmsg = "";
            string finalFilePath = null;
            string resulturl = null;
            try {
                var guid = Request.Form["guid"];//临时文件夹
                string fileName = Request.Form["fileName"];//文件名
                var tmpPath_File = fileName.Split('.')[0];
                var temporary = Path.Combine(UploadConfig.TmpPath, tmpPath_File);//临时文件夹
                string fileExt = Path.GetExtension(fileName);//获取文件后缀
                var files = Directory.GetFiles(temporary);//获得下面的所有文件

                #region "设置要保存的路径"
                UploadConfig.UpLoadPath += fileExt.Split('.')[1] + @"/";
                if (!System.IO.Directory.Exists(UploadConfig.UpLoadPath)) {
                    System.IO.Directory.CreateDirectory(UploadConfig.UpLoadPath);
                }
                #endregion  "设置要保存的路径"

                finalFilePath = Path.Combine(UploadConfig.UpLoadPath + fileName);//最终的文件名
                resulturl = fileExt.Split('.')[1] + @"/" + fileName;
                using (var fs = new FileStream(finalFilePath, FileMode.Create)) {
                    foreach (var part in files.OrderBy(x => x.Length).ThenBy(x => x)) {
                        var bytes = System.IO.File.ReadAllBytes(part);
                        await fs.WriteAsync(bytes, 0, bytes.Length);
                        bytes = null;
                        System.IO.File.Delete(part);//删除分块
                    }
                    Directory.Delete(temporary);//删除文件夹
                    ok = true;
                }
            } catch (Exception ex) {
                ok = false;
                errmsg = ex.Message;
            } finally {
            }
            if (ok) {
                return Ok(new { success = true, msg = "", url = resulturl });
            } else {
                return Ok(new { success = false, msg = errmsg, url = "unknown" });
            }
        }

        private static char[] constant = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z' };
        public static string GenerateRandomString(int length) {
            string checkCode = String.Empty;
            Random rd = new Random();
            for (int i = 0; i < length; i++) {
                checkCode += constant[rd.Next(36)].ToString();
            }
            return checkCode;
        }


        //http://camefor.top:85/FilePath/379a8c403f8c4f9487a33ae975923b80.mp4
        //http://camefor.top:85/FilePath/2abf7c2eb2044f7ca417d683dda3d831.mp4
        //http://camefor.top:85/FilePath/a2e4d8c3dcf647b084ff9e9af232068c.mp4

    }
}