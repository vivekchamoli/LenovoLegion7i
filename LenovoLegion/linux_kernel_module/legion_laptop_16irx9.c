// legion_laptop_16irx9.c - Linux kernel module for Legion Slim 7i Gen 9
#include <linux/module.h>
#include <linux/kernel.h>
#include <linux/init.h>
#include <linux/acpi.h>
#include <linux/platform_device.h>
#include <linux/hwmon.h>
#include <linux/hwmon-sysfs.h>
#include <linux/dmi.h>
#include <linux/io.h>
#include <linux/delay.h>
#include <linux/mutex.h>
#include <linux/sysfs.h>
#include <linux/thermal.h>

#define DRIVER_NAME "legion_laptop_16irx9"
#define EC_PORT_CMD 0x66
#define EC_PORT_DATA 0x62

MODULE_LICENSE("GPL");
MODULE_AUTHOR("LenovoLegionToolkit");
MODULE_DESCRIPTION("Legion Slim 7i Gen 9 (16IRX9) Laptop Support Driver");
MODULE_VERSION("1.0.0");

// EC registers for Gen 9 (matching the Windows implementation)
enum gen9_ec_registers {
    EC_REG_PERFORMANCE_MODE = 0xA0,
    EC_REG_AI_ENGINE = 0xA1,
    EC_REG_THERMAL_MODE = 0xA2,
    EC_REG_CUSTOM_TDP = 0xA4,

    EC_REG_FAN1_SPEED = 0xB0,
    EC_REG_FAN2_SPEED = 0xB1,
    EC_REG_FAN1_TARGET = 0xB2,
    EC_REG_FAN2_TARGET = 0xB3,

    EC_REG_CPU_PL1 = 0xC0,
    EC_REG_CPU_PL2 = 0xC1,
    EC_REG_GPU_TGP = 0xC4,

    EC_REG_CPU_TEMP = 0xE0,
    EC_REG_GPU_TEMP = 0xE2,
    EC_REG_GPU_HOTSPOT = 0xE3,
    EC_REG_VRM_TEMP = 0xE5,
    EC_REG_SSD_TEMP = 0xE6,

    EC_REG_RGB_MODE = 0xF0,
    EC_REG_RGB_BRIGHTNESS = 0xF1,
};

static DEFINE_MUTEX(ec_mutex);

struct legion_laptop {
    struct platform_device *pdev;
    struct device *hwmon_dev;
    struct thermal_zone_device *thermal_zone;
    struct led_classdev kbd_led;

    // Cached values
    u8 performance_mode;
    u8 fan1_speed;
    u8 fan2_speed;
    u8 cpu_temp;
    u8 gpu_temp;
};

static struct legion_laptop *legion_dev;

// EC communication functions
static int ec_wait(void)
{
    int i;
    for (i = 0; i < 1000; i++) {
        if ((inb(EC_PORT_CMD) & 0x02) == 0)
            return 0;
        udelay(10);
    }
    return -ETIMEDOUT;
}

static int ec_read(u8 reg, u8 *value)
{
    int ret;

    mutex_lock(&ec_mutex);

    ret = ec_wait();
    if (ret)
        goto out;

    outb(0x80, EC_PORT_CMD);

    ret = ec_wait();
    if (ret)
        goto out;

    outb(reg, EC_PORT_DATA);

    ret = ec_wait();
    if (ret)
        goto out;

    *value = inb(EC_PORT_DATA);

out:
    mutex_unlock(&ec_mutex);
    return ret;
}

static int ec_write(u8 reg, u8 value)
{
    int ret;

    mutex_lock(&ec_mutex);

    ret = ec_wait();
    if (ret)
        goto out;

    outb(0x81, EC_PORT_CMD);

    ret = ec_wait();
    if (ret)
        goto out;

    outb(reg, EC_PORT_DATA);

    ret = ec_wait();
    if (ret)
        goto out;

    outb(value, EC_PORT_DATA);

    ret = ec_wait();

out:
    mutex_unlock(&ec_mutex);
    return ret;
}

// Performance mode control
static ssize_t performance_mode_show(struct device *dev,
                                    struct device_attribute *attr, char *buf)
{
    u8 mode;
    int ret = ec_read(EC_REG_PERFORMANCE_MODE, &mode);
    if (ret)
        return ret;

    const char *mode_str;
    switch (mode) {
        case 0: mode_str = "quiet"; break;
        case 1: mode_str = "balanced"; break;
        case 2: mode_str = "performance"; break;
        case 3: mode_str = "custom"; break;
        default: mode_str = "unknown"; break;
    }

    return sprintf(buf, "%s\n", mode_str);
}

static ssize_t performance_mode_store(struct device *dev,
                                     struct device_attribute *attr,
                                     const char *buf, size_t count)
{
    u8 mode;

    if (sysfs_streq(buf, "quiet"))
        mode = 0;
    else if (sysfs_streq(buf, "balanced"))
        mode = 1;
    else if (sysfs_streq(buf, "performance"))
        mode = 2;
    else if (sysfs_streq(buf, "custom"))
        mode = 3;
    else
        return -EINVAL;

    int ret = ec_write(EC_REG_PERFORMANCE_MODE, mode);
    if (ret)
        return ret;

    legion_dev->performance_mode = mode;
    return count;
}

static DEVICE_ATTR_RW(performance_mode);

// Fan control
static ssize_t fan1_speed_show(struct device *dev,
                               struct device_attribute *attr, char *buf)
{
    u8 speed;
    int ret = ec_read(EC_REG_FAN1_SPEED, &speed);
    if (ret)
        return ret;

    // Convert to RPM (speed * 100)
    return sprintf(buf, "%d\n", speed * 100);
}

static ssize_t fan1_target_store(struct device *dev,
                                 struct device_attribute *attr,
                                 const char *buf, size_t count)
{
    unsigned long target;
    if (kstrtoul(buf, 10, &target))
        return -EINVAL;

    if (target > 100)
        return -EINVAL;

    int ret = ec_write(EC_REG_FAN1_TARGET, (u8)target);
    if (ret)
        return ret;

    return count;
}

static ssize_t fan2_speed_show(struct device *dev,
                               struct device_attribute *attr, char *buf)
{
    u8 speed;
    int ret = ec_read(EC_REG_FAN2_SPEED, &speed);
    if (ret)
        return ret;

    return sprintf(buf, "%d\n", speed * 100);
}

static ssize_t fan2_target_store(struct device *dev,
                                 struct device_attribute *attr,
                                 const char *buf, size_t count)
{
    unsigned long target;
    if (kstrtoul(buf, 10, &target))
        return -EINVAL;

    if (target > 100)
        return -EINVAL;

    int ret = ec_write(EC_REG_FAN2_TARGET, (u8)target);
    if (ret)
        return ret;

    return count;
}

static DEVICE_ATTR_RO(fan1_speed);
static DEVICE_ATTR_WO(fan1_target);
static DEVICE_ATTR_RO(fan2_speed);
static DEVICE_ATTR_WO(fan2_target);

// Temperature monitoring
static ssize_t cpu_temp_show(struct device *dev,
                             struct device_attribute *attr, char *buf)
{
    u8 temp;
    int ret = ec_read(EC_REG_CPU_TEMP, &temp);
    if (ret)
        return ret;

    return sprintf(buf, "%d\n", temp);
}

static ssize_t gpu_temp_show(struct device *dev,
                             struct device_attribute *attr, char *buf)
{
    u8 temp;
    int ret = ec_read(EC_REG_GPU_TEMP, &temp);
    if (ret)
        return ret;

    return sprintf(buf, "%d\n", temp);
}

static ssize_t gpu_hotspot_show(struct device *dev,
                                struct device_attribute *attr, char *buf)
{
    u8 temp;
    int ret = ec_read(EC_REG_GPU_HOTSPOT, &temp);
    if (ret)
        return ret;

    return sprintf(buf, "%d\n", temp);
}

static ssize_t vrm_temp_show(struct device *dev,
                             struct device_attribute *attr, char *buf)
{
    u8 temp;
    int ret = ec_read(EC_REG_VRM_TEMP, &temp);
    if (ret)
        return ret;

    return sprintf(buf, "%d\n", temp);
}

static ssize_t ssd_temp_show(struct device *dev,
                             struct device_attribute *attr, char *buf)
{
    u8 temp;
    int ret = ec_read(EC_REG_SSD_TEMP, &temp);
    if (ret)
        return ret;

    return sprintf(buf, "%d\n", temp);
}

static DEVICE_ATTR_RO(cpu_temp);
static DEVICE_ATTR_RO(gpu_temp);
static DEVICE_ATTR_RO(gpu_hotspot);
static DEVICE_ATTR_RO(vrm_temp);
static DEVICE_ATTR_RO(ssd_temp);

// Power limit control
static ssize_t cpu_pl1_store(struct device *dev,
                             struct device_attribute *attr,
                             const char *buf, size_t count)
{
    unsigned long pl1;
    if (kstrtoul(buf, 10, &pl1))
        return -EINVAL;

    if (pl1 > 140)  // Max 140W for i9-14900HX
        return -EINVAL;

    int ret = ec_write(EC_REG_CPU_PL1, (u8)pl1);
    if (ret)
        return ret;

    return count;
}

static ssize_t cpu_pl2_store(struct device *dev,
                             struct device_attribute *attr,
                             const char *buf, size_t count)
{
    unsigned long pl2;
    if (kstrtoul(buf, 10, &pl2))
        return -EINVAL;

    if (pl2 > 200)  // Max 200W turbo
        return -EINVAL;

    int ret = ec_write(EC_REG_CPU_PL2, (u8)pl2);
    if (ret)
        return ret;

    return count;
}

static ssize_t gpu_tgp_store(struct device *dev,
                             struct device_attribute *attr,
                             const char *buf, size_t count)
{
    unsigned long tgp;
    if (kstrtoul(buf, 10, &tgp))
        return -EINVAL;

    if (tgp > 140)  // Max 140W for RTX 4070
        return -EINVAL;

    int ret = ec_write(EC_REG_GPU_TGP, (u8)tgp);
    if (ret)
        return ret;

    return count;
}

static DEVICE_ATTR_WO(cpu_pl1);
static DEVICE_ATTR_WO(cpu_pl2);
static DEVICE_ATTR_WO(gpu_tgp);

// RGB control
static ssize_t rgb_mode_store(struct device *dev,
                              struct device_attribute *attr,
                              const char *buf, size_t count)
{
    u8 mode;

    if (sysfs_streq(buf, "off"))
        mode = 0;
    else if (sysfs_streq(buf, "static"))
        mode = 1;
    else if (sysfs_streq(buf, "breathing"))
        mode = 2;
    else if (sysfs_streq(buf, "rainbow"))
        mode = 3;
    else if (sysfs_streq(buf, "wave"))
        mode = 4;
    else
        return -EINVAL;

    int ret = ec_write(EC_REG_RGB_MODE, mode);
    if (ret)
        return ret;

    return count;
}

static ssize_t rgb_brightness_store(struct device *dev,
                                    struct device_attribute *attr,
                                    const char *buf, size_t count)
{
    unsigned long brightness;
    if (kstrtoul(buf, 10, &brightness))
        return -EINVAL;

    if (brightness > 100)
        return -EINVAL;

    int ret = ec_write(EC_REG_RGB_BRIGHTNESS, (u8)brightness);
    if (ret)
        return ret;

    return count;
}

static DEVICE_ATTR_WO(rgb_mode);
static DEVICE_ATTR_WO(rgb_brightness);

// Gen 9 hardware fixes implementation
static ssize_t apply_gen9_fixes_store(struct device *dev,
                                      struct device_attribute *attr,
                                      const char *buf, size_t count)
{
    unsigned long enable;
    if (kstrtoul(buf, 10, &enable))
        return -EINVAL;

    if (enable) {
        // Apply thermal throttling fix
        ec_write(0xD0, 0x69);  // CPU TjMax to 105°C
        ec_write(0xD2, 0x05);  // 5°C throttle offset
        ec_write(0xD3, 0x02);  // Enhanced vapor chamber mode
        ec_write(0xD4, 0x0A);  // Aggressive thermal velocity

        // Apply optimized fan curve
        ec_write(0xB6, 0x02);  // Fan hysteresis
        ec_write(0xB7, 0x03);  // Fast fan acceleration
        ec_write(0xB8, 0x01);  // Enable zero RPM mode

        // Optimize core scheduling
        ec_write(0xC7, 0x39);  // P-core ratio (5.7GHz)
        ec_write(0xC8, 0x2C);  // E-core ratio (4.4GHz)
        ec_write(0xC9, 0x32);  // Cache ratio

        dev_info(dev, "Legion Slim 7i Gen 9 hardware fixes applied\n");
    }

    return count;
}

static DEVICE_ATTR_WO(apply_gen9_fixes);

static struct attribute *legion_attrs[] = {
    &dev_attr_performance_mode.attr,
    &dev_attr_fan1_speed.attr,
    &dev_attr_fan1_target.attr,
    &dev_attr_fan2_speed.attr,
    &dev_attr_fan2_target.attr,
    &dev_attr_cpu_temp.attr,
    &dev_attr_gpu_temp.attr,
    &dev_attr_gpu_hotspot.attr,
    &dev_attr_vrm_temp.attr,
    &dev_attr_ssd_temp.attr,
    &dev_attr_cpu_pl1.attr,
    &dev_attr_cpu_pl2.attr,
    &dev_attr_gpu_tgp.attr,
    &dev_attr_rgb_mode.attr,
    &dev_attr_rgb_brightness.attr,
    &dev_attr_apply_gen9_fixes.attr,
    NULL,
};

static const struct attribute_group legion_attr_group = {
    .attrs = legion_attrs,
};

// DMI matching for Legion Slim 7i Gen 9
static const struct dmi_system_id legion_dmi_table[] = {
    {
        .matches = {
            DMI_MATCH(DMI_SYS_VENDOR, "LENOVO"),
            DMI_MATCH(DMI_PRODUCT_NAME, "16IRX9"),
        },
    },
    {
        .matches = {
            DMI_MATCH(DMI_SYS_VENDOR, "LENOVO"),
            DMI_MATCH(DMI_PRODUCT_VERSION, "Legion Slim 7i Gen 9"),
        },
    },
    {
        .matches = {
            DMI_MATCH(DMI_SYS_VENDOR, "LENOVO"),
            DMI_MATCH(DMI_BOARD_NAME, "LNVNB161216"),
        },
    },
    {}
};

// Hwmon integration for temperature monitoring
static umode_t legion_hwmon_is_visible(const void *data,
                                       enum hwmon_sensor_types type,
                                       u32 attr, int channel)
{
    return 0444;
}

static int legion_hwmon_read(struct device *dev, enum hwmon_sensor_types type,
                             u32 attr, int channel, long *val)
{
    u8 temp;
    int ret;

    switch (type) {
    case hwmon_temp:
        switch (channel) {
        case 0: // CPU
            ret = ec_read(EC_REG_CPU_TEMP, &temp);
            break;
        case 1: // GPU
            ret = ec_read(EC_REG_GPU_TEMP, &temp);
            break;
        case 2: // GPU Hotspot
            ret = ec_read(EC_REG_GPU_HOTSPOT, &temp);
            break;
        case 3: // VRM
            ret = ec_read(EC_REG_VRM_TEMP, &temp);
            break;
        case 4: // SSD
            ret = ec_read(EC_REG_SSD_TEMP, &temp);
            break;
        default:
            return -EOPNOTSUPP;
        }

        if (ret)
            return ret;

        *val = temp * 1000; // Convert to millidegrees
        return 0;

    case hwmon_fan:
        switch (channel) {
        case 0: // Fan 1
            ret = ec_read(EC_REG_FAN1_SPEED, &temp);
            break;
        case 1: // Fan 2
            ret = ec_read(EC_REG_FAN2_SPEED, &temp);
            break;
        default:
            return -EOPNOTSUPP;
        }

        if (ret)
            return ret;

        *val = temp * 100; // Convert to RPM
        return 0;

    default:
        return -EOPNOTSUPP;
    }
}

static const struct hwmon_ops legion_hwmon_ops = {
    .is_visible = legion_hwmon_is_visible,
    .read = legion_hwmon_read,
};

static const struct hwmon_channel_info *legion_hwmon_info[] = {
    HWMON_CHANNEL_INFO(temp,
                       HWMON_T_INPUT | HWMON_T_LABEL, // CPU
                       HWMON_T_INPUT | HWMON_T_LABEL, // GPU
                       HWMON_T_INPUT | HWMON_T_LABEL, // GPU Hotspot
                       HWMON_T_INPUT | HWMON_T_LABEL, // VRM
                       HWMON_T_INPUT | HWMON_T_LABEL), // SSD
    HWMON_CHANNEL_INFO(fan,
                       HWMON_F_INPUT | HWMON_F_LABEL, // Fan 1
                       HWMON_F_INPUT | HWMON_F_LABEL), // Fan 2
    NULL
};

static const struct hwmon_chip_info legion_hwmon_chip_info = {
    .ops = &legion_hwmon_ops,
    .info = legion_hwmon_info,
};

static int legion_laptop_probe(struct platform_device *pdev)
{
    int ret;

    legion_dev = devm_kzalloc(&pdev->dev, sizeof(*legion_dev), GFP_KERNEL);
    if (!legion_dev)
        return -ENOMEM;

    legion_dev->pdev = pdev;
    platform_set_drvdata(pdev, legion_dev);

    // Create sysfs attributes
    ret = sysfs_create_group(&pdev->dev.kobj, &legion_attr_group);
    if (ret) {
        dev_err(&pdev->dev, "Failed to create sysfs attributes\n");
        return ret;
    }

    // Register hwmon device
    legion_dev->hwmon_dev = devm_hwmon_device_register_with_info(
        &pdev->dev, "legion_laptop", legion_dev, &legion_hwmon_chip_info, NULL);

    if (IS_ERR(legion_dev->hwmon_dev)) {
        ret = PTR_ERR(legion_dev->hwmon_dev);
        dev_err(&pdev->dev, "Failed to register hwmon device\n");
        goto err_sysfs;
    }

    // Apply Gen 9 hardware fixes on load
    ec_write(0xD0, 0x69);  // CPU TjMax to 105°C
    ec_write(0xD3, 0x02);  // Enhanced vapor chamber mode
    ec_write(0xB8, 0x01);  // Enable zero RPM mode

    dev_info(&pdev->dev, "Legion Slim 7i Gen 9 (16IRX9) driver loaded successfully\n");
    return 0;

err_sysfs:
    sysfs_remove_group(&pdev->dev.kobj, &legion_attr_group);
    return ret;
}

static int legion_laptop_remove(struct platform_device *pdev)
{
    sysfs_remove_group(&pdev->dev.kobj, &legion_attr_group);
    dev_info(&pdev->dev, "Legion Slim 7i Gen 9 driver removed\n");
    return 0;
}

static struct platform_driver legion_laptop_driver = {
    .driver = {
        .name = DRIVER_NAME,
    },
    .probe = legion_laptop_probe,
    .remove = legion_laptop_remove,
};

static struct platform_device *legion_platform_device;

static int __init legion_laptop_init(void)
{
    int ret;

    // Check if we're running on a Legion Slim 7i Gen 9
    if (!dmi_check_system(legion_dmi_table)) {
        pr_info("This machine is not a Legion Slim 7i Gen 9 (16IRX9)\n");
        return -ENODEV;
    }

    // Request EC port access
    if (!request_region(EC_PORT_CMD, 1, DRIVER_NAME)) {
        pr_err("Failed to request EC command port\n");
        return -EBUSY;
    }

    if (!request_region(EC_PORT_DATA, 1, DRIVER_NAME)) {
        pr_err("Failed to request EC data port\n");
        release_region(EC_PORT_CMD, 1);
        return -EBUSY;
    }

    // Register platform driver
    ret = platform_driver_register(&legion_laptop_driver);
    if (ret) {
        pr_err("Failed to register platform driver\n");
        goto err_regions;
    }

    // Create platform device
    legion_platform_device = platform_device_register_simple(
        DRIVER_NAME, -1, NULL, 0);

    if (IS_ERR(legion_platform_device)) {
        ret = PTR_ERR(legion_platform_device);
        pr_err("Failed to register platform device\n");
        goto err_driver;
    }

    pr_info("Legion Slim 7i Gen 9 (16IRX9) kernel module loaded\n");
    return 0;

err_driver:
    platform_driver_unregister(&legion_laptop_driver);
err_regions:
    release_region(EC_PORT_DATA, 1);
    release_region(EC_PORT_CMD, 1);
    return ret;
}

static void __exit legion_laptop_exit(void)
{
    platform_device_unregister(legion_platform_device);
    platform_driver_unregister(&legion_laptop_driver);
    release_region(EC_PORT_DATA, 1);
    release_region(EC_PORT_CMD, 1);
    pr_info("Legion Slim 7i Gen 9 (16IRX9) kernel module unloaded\n");
}

module_init(legion_laptop_init);
module_exit(legion_laptop_exit);

MODULE_DEVICE_TABLE(dmi, legion_dmi_table);
MODULE_ALIAS("platform:" DRIVER_NAME);