const { createApp, ref, onMounted, onUnmounted } = Vue;

const app = createApp({
    setup() {
        // 状态
        const status = ref({
            isAccelerating: false,
            enabledServiceCount: 0,
            selectedIpCount: 0,
            selectedIps: {},
            lastSpeedTest: null,
            proxyRunning: false,
            proxyConnections: 0,
            isAdmin: false,
            webPort: 2606,
        });
        
        const services = ref([]);
        const config = ref({
            proxyEnabled: true,
            autoStart: false,
            autoTestIntervalHours: 24,
        });
        const logs = ref([]);
        const testing = ref(false);
        const showIps = ref(false);

        // 流量统计
        const traffic = ref({
            totalConnections: 0,
            activeConnections: 0,
            totalBytesReceived: 0,
            totalBytesSent: 0,
        });

        // 失联检测
        const disconnected = ref(false);
        let healthCheckTimer = null;
        let healthFailCount = 0;
        const MAX_HEALTH_FAILS = 3;

        // 流量轮询
        let trafficTimer = null;

        // API 请求封装
        const api = async (method, path, body = null) => {
            try {
                const options = {
                    method,
                    headers: { 'Content-Type': 'application/json' },
                };
                if (body) {
                    options.body = JSON.stringify(body);
                }
                const response = await fetch(`/api${path}`, options);
                if (!response.ok) {
                    throw new Error(`HTTP ${response.status}`);
                }
                return await response.json();
            } catch (error) {
                console.error(`API Error: ${method} ${path}`, error);
                showToast(`请求失败: ${error.message}`, 'error');
                throw error;
            }
        };

        // 刷新状态
        const refreshStatus = async () => {
            try {
                const data = await api('GET', '/status');
                status.value = data;
            } catch (e) {
                // 静默处理
            }
        };

        // 刷新服务列表
        const refreshServices = async () => {
            try {
                const data = await api('GET', '/services');
                services.value = data;
            } catch (e) {
                // 静默处理
            }
        };

        // 刷新配置
        const refreshConfig = async () => {
            try {
                const data = await api('GET', '/config');
                config.value = {
                    proxyEnabled: data.proxyEnabled,
                    autoStart: data.autoStart,
                    autoTestIntervalHours: data.autoTestIntervalHours,
                };
            } catch (e) {
                // 静默处理
            }
        };

        // 刷新流量统计
        const refreshTraffic = async () => {
            if (!status.value.proxyRunning && !status.value.isAccelerating) {
                return;
            }
            try {
                const data = await api('GET', '/stats/traffic');
                if (data) {
                    traffic.value = data;
                }
            } catch (e) {
                // 静默处理
            }
        };

        // 健康检查
        const checkHealth = async () => {
            try {
                const response = await fetch('/api/ping');
                if (response.ok) {
                    healthFailCount = 0;
                    if (disconnected.value) {
                        disconnected.value = false;
                        showToast('客户端已重新连接', 'success');
                    }
                } else {
                    throw new Error('not ok');
                }
            } catch (e) {
                healthFailCount++;
                if (healthFailCount >= MAX_HEALTH_FAILS) {
                    disconnected.value = true;
                }
            }
        };

        // 切换加速状态
        const toggleAcceleration = async () => {
            if (status.value.isAccelerating) {
                await api('POST', '/stop');
                showToast('加速已停止');
            } else {
                await api('POST', '/start');
                showToast('加速已启动');
            }
            await refreshStatus();
            await refreshLogs();
            await refreshTraffic();
        };

        // 运行测速
        const runSpeedTest = async () => {
            testing.value = true;
            try {
                await api('POST', '/speedtest');
                showToast('测速已开始，请稍候...');
                
                // 轮询测速结果
                const pollInterval = setInterval(async () => {
                    await refreshStatus();
                    await refreshLogs();
                    
                    // 如果有新的测速结果，停止轮询
                    if (status.value.lastSpeedTest) {
                        clearInterval(pollInterval);
                        testing.value = false;
                    }
                }, 2000);
                
                // 5分钟后停止轮询
                setTimeout(() => {
                    clearInterval(pollInterval);
                    testing.value = false;
                }, 300000);
            } catch (e) {
                testing.value = false;
            }
        };

        // 切换服务
        const toggleService = async (service) => {
            try {
                await api('POST', `/services/${service.id}/toggle`, {
                    enabled: !service.enabled,
                });
                await refreshServices();
                await refreshStatus();
                showToast(`${service.name} 已${service.enabled ? '禁用' : '启用'}`);
            } catch (e) {
                // 静默处理
            }
        };

        // 全选
        const selectAll = async () => {
            const allEnabled = services.value.every(s => s.enabled);
            for (const service of services.value) {
                if (allEnabled ? service.enabled : !service.enabled) {
                    await api('POST', `/services/${service.id}/toggle`, {
                        enabled: !allEnabled,
                    });
                }
            }
            await refreshServices();
            await refreshStatus();
            showToast(allEnabled ? '已全部禁用' : '已全部启用');
        };

        // 保存配置
        const saveConfig = async () => {
            try {
                await api('POST', '/config', config.value);
                showToast('配置已保存');
            } catch (e) {
                // 静默处理
            }
        };

        // 刷新日志
        const refreshLogs = async () => {
            try {
                const data = await api('GET', '/logs');
                logs.value = data;
            } catch (e) {
                // 静默处理
            }
        };

        // 格式化时间
        const formatTime = (time) => {
            if (!time) return '从未';
            const date = new Date(time);
            const now = new Date();
            const diff = now - date;
            
            if (diff < 60000) return '刚刚';
            if (diff < 3600000) return `${Math.floor(diff / 60000)} 分钟前`;
            if (diff < 86400000) return `${Math.floor(diff / 3600000)} 小时前`;
            return date.toLocaleDateString('zh-CN');
        };

        // 格式化字节数
        const formatBytes = (bytes) => {
            if (!bytes || bytes === 0) return '0 B';
            if (bytes < 1024) return `${bytes} B`;
            if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
            if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
            return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
        };

        // Toast 提示
        let toastTimer = null;
        const showToast = (message, type = 'success') => {
            const oldToast = document.querySelector('.toast');
            if (oldToast) oldToast.remove();
            
            const toast = document.createElement('div');
            toast.className = `toast ${type}`;
            toast.textContent = message;
            document.body.appendChild(toast);
            
            if (toastTimer) clearTimeout(toastTimer);
            toastTimer = setTimeout(() => {
                toast.remove();
            }, 3000);
        };

        // 初始化
        onMounted(async () => {
            await refreshStatus();
            await refreshServices();
            await refreshConfig();
            await refreshLogs();
            await refreshTraffic();
            
            // 定期刷新状态
            setInterval(async () => {
                await refreshStatus();
            }, 5000);

            // 定期刷新流量
            trafficTimer = setInterval(async () => {
                await refreshTraffic();
            }, 3000);

            // 健康检查（每10秒检测一次）
            healthCheckTimer = setInterval(async () => {
                await checkHealth();
            }, 10000);
        });

        onUnmounted(() => {
            if (healthCheckTimer) clearInterval(healthCheckTimer);
            if (trafficTimer) clearInterval(trafficTimer);
        });

        return {
            status,
            services,
            config,
            logs,
            testing,
            showIps,
            traffic,
            disconnected,
            toggleAcceleration,
            runSpeedTest,
            toggleService,
            selectAll,
            saveConfig,
            refreshLogs,
            formatTime,
            formatBytes,
        };
    }
});

app.mount('#app');
