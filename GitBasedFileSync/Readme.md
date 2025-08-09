# GitBasedFileSync

一个基于Git的文件同步工具，支持定时任务自动同步和版本记录。仅支持Windows 10及以上系统。

基本原理：备份端安装Git服务端，客户端通过git push自动同步文件到Git服务端。

使用Git LFS（Large File Storage）功能，支持大文件的版本控制。减少备份端多版本的存储空间占用。

如果需要多端备份，其他设备克隆远程仓库后安装使用本软件即可。但要注意不要多个客户端在同步空窗期修改同一文件，否则可能会冲突。若出现冲突必须手动解决。

适用场景：软件配置文件、游戏存档、文本资料，或大文件的自动备份。

不适用场景：较大且频繁修改的二进制文件（多版本备份占用磁盘空间较多）。

缺点：备份端不能从磁盘直接看到备份文件内容，需通过git查看。

# 服务端配置

## 使用gogs作为git服务器

### 使用docker部署gogs

> 参考 [Gogs官方文档](https://github.com/gogs/gogs/blob/main/docker/README.md)

```bash
# 拉取gogs镜像
docker pull gogs/gogs

# 创建本地gogs数据目录，可以换成别的
# 如果需要备份gogs数据，则备份这个文件夹即可
mkdir -p /var/gogs

# 启动gogs容器，确保本机gogs数据目录挂载到容器内的/data目录
# 为了避免不必要的麻烦，容器内外都使用默认的3000端口
# 不需要ssh功能，不映射22端口
docker run --name=gogs -p 3000:3000 -v /var/gogs:/data gogs/gogs
```

容器启动后，访问 http://x.x.x.x:3000 即可看到gogs的初始化界面

### 初始化gogs

设置数据库，直接使用sqlite3即可，数据库文件路径改成`/data/gogs/data/gogs.db`。
如果要使用其他数据库，请自行配置。注意备份数据库。

应用基本设置里面，按自己的需求设置。注意域名设置，设置不对可能会导致LFS功能异常。
SSH端口号直接删除，不需要使用SSH功能。

下方可选设置中，服务器和其他服务设置，建议开启禁止用户自主注册功能。管理员设置中，添加一个管理员账号。

### 创建仓库

登录gogs，点击右上角的“+”号，选择“创建新的仓库”，填写仓库名称和描述，直接创建即可。
> 每个同步任务，都需要先创建一个仓库

## 使用其他git服务器

如果使用其他git服务器，请自行安装，确保服务器支持LFS功能。

# 客户端配置

## 安装git

自行安装git客户端，尽量使用新版本。

## APP配置

配置文件为`app-settings.conf`，使用HOCON格式。

**配置文件不支持热加载，所有修改均需要重启APP。**

```hocon
app {
    tasks = [
        {
            // 任务名称，不能重复
            name = "test"

            // 需要同步的本地目录，必须是绝对路径，必须已经存在
            // Windows系统下反斜杠`\`需转义，写成`\\`
            path = "F:\\test\\"

            // 远程Git仓库地址，需自己新建仓库
            repo = "http://x.x.x.x:3000/test/test.git"

            // 定时任务的cron表达式，使用Quartz 6段cron表达式
            cron = "0 0 18 * * ?"

            // 同步时忽略的文件或目录，字符串数组，参考 .gitignore 文件规则
            // 如果是已经被Git跟踪的文件或目录，则再新增gitignore也不会被忽略，可通过手动执行命令来取消跟踪
            // 可不填
            ignore = ["**/temp"]

            // 需要使用LFS进行同步的文件或目录，字符串数组，参考 git lfs track xxx 命令。
            // 不会自动处理LFS规则减少的情况。
            // 若需要去除某些文件的LFS跟踪，需手动操作。
            // 可不填
            lfs = ["*.jpg"]

            // 是否在同步成功后发送通知。若同步时无变更，则不会发送通知。
            // 可不填，默认为true
            notifyWhenSuccess = true
        }
    ]

    setting {
        // 是否开启应用调试模式，调试模式会跳过同步任务的加载、执行
        // 仅用于开发，便于确认应用自身的功能是否正常
        // 默认为false
        debugAppMode = false

        // 是否开启效率模式，效率模式下，应用会减少部分资源占用
        // 默认为true
        ecoMode = true
    }
}
```

## 运行APP

双击`GitBasedFileSync.exe`运行。
