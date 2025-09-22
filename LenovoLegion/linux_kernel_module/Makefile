# Makefile for Legion Slim 7i Gen 9 (16IRX9) Linux kernel module

obj-m += legion_laptop_16irx9.o

# Get kernel build directory
KDIR ?= /lib/modules/$(shell uname -r)/build

# Build targets
all:
	$(MAKE) -C $(KDIR) M=$(PWD) modules

clean:
	$(MAKE) -C $(KDIR) M=$(PWD) clean

install: all
	$(MAKE) -C $(KDIR) M=$(PWD) modules_install
	depmod -a

# DKMS support
dkms-install:
	dkms add .
	dkms build legion-laptop/1.0.0
	dkms install legion-laptop/1.0.0

dkms-remove:
	dkms remove legion-laptop/1.0.0 --all

.PHONY: all clean install dkms-install dkms-remove