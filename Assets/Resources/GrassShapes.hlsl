            float PixelRect(float2 pixel,float4 rect)
            {
                return step(rect.x,pixel.x)*step(rect.y,pixel.y)*
                    (1-step(rect.x+rect.z,pixel.x))*(1-step(rect.y+rect.w,pixel.y));
            }

            float ShapeMask(float3 shapeUV)
            {
                int type=(int)round(shapeUV.z);
                float2 dimensions=type==0?float2(11,9):
                    (type==1?float2(13,9):float2(14,13));
                float2 pixel=min(floor(saturate(shapeUV.xy)*dimensions),dimensions-1);
                float mask=0;
                if(type==0)
                {
                    mask+=PixelRect(pixel,float4(0,4,1,3));
                    mask+=PixelRect(pixel,float4(1,0,1,5));
                    mask+=PixelRect(pixel,float4(4,0,1,9));
                    mask+=PixelRect(pixel,float4(6,0,1,3));
                    mask+=PixelRect(pixel,float4(7,2,1,5));
                    mask+=PixelRect(pixel,float4(10,0,1,7));
                }
                else if(type==1)
                {
                    mask+=PixelRect(pixel,float4(0,3,2,2));
                    mask+=PixelRect(pixel,float4(1,1,2,2));
                    mask+=PixelRect(pixel,float4(2,0,2,1));
                    mask+=PixelRect(pixel,float4(4,0,1,9));
                    mask+=PixelRect(pixel,float4(3,7,1,2));
                    mask+=PixelRect(pixel,float4(7,0,1,5));
                    mask+=PixelRect(pixel,float4(8,3,1,2));
                    mask+=PixelRect(pixel,float4(11,0,1,8));
                    mask+=PixelRect(pixel,float4(12,6,1,2));
                }
                else
                {
                    mask+=PixelRect(pixel,float4(0,4,2,3));
                    mask+=PixelRect(pixel,float4(1,7,1,1));
                    mask+=PixelRect(pixel,float4(1,3,2,1));
                    mask+=PixelRect(pixel,float4(2,0,1,3));
                    mask+=PixelRect(pixel,float4(3,11,1,2));
                    mask+=PixelRect(pixel,float4(4,12,1,1));
                    mask+=PixelRect(pixel,float4(5,0,1,13));
                    mask+=PixelRect(pixel,float4(7,0,1,5));
                    mask+=PixelRect(pixel,float4(8,4,1,2));
                    mask+=PixelRect(pixel,float4(9,5,1,1));
                    mask+=PixelRect(pixel,float4(10,6,1,1));
                    mask+=PixelRect(pixel,float4(11,4,1,1));
                    mask+=PixelRect(pixel,float4(12,0,1,4));
                    mask+=PixelRect(pixel,float4(13,3,1,1));
                }
                return saturate(mask);
            }

